using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	private static string ContentsText(JsonElement request) => string.Join("\n", request.GetProperty("contents").EnumerateArray()
		.SelectMany(content => content.GetProperty("parts").EnumerateArray())
		.Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString()));

	// Scripted Gemini responses exercise declarations, history and tool routing. They do not
	// evaluate a live model's semantic matching or interpretation of natural-language consent.
	[Theory]
	[InlineData(PlanningMode.General)]
	[InlineData(PlanningMode.WeekPlanning)]
	[InlineData(PlanningMode.DayWork)]
	public async Task NewTaskSuggestionLeavesTaskUnlinkedUntilNextTurnConfirms(PlanningMode mode)
	{
		var task = Guid.NewGuid(); var goal = Guid.NewGuid(); var list = Guid.NewGuid();
		var script = new List<string>();
		if (mode == PlanningMode.General) script.Add(Calls(Call("skill_load", new { skillId = "task_management" })));
		script.Add(Calls(Call("taskitem_create", new { title = "Book physiotherapy", taskListId = list })));
		script.Add(Calls(Call("goal_list", new { includeInactive = false })));
		script.Add(Final("Added Book physiotherapy. Link it to Run a half marathon?"));
		if (mode == PlanningMode.General) script.Add(Calls(Call("skill_load", new { skillId = "task_management" })));
		script.Add(Calls(Call("taskitem_link_goal", new { taskId = task, goalId = goal })));
		script.Add(Final("Linked to Run a half marathon."));
		using var provider = new ScriptedProvider(script.ToArray());
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_create"] = JsonSerializer.Serialize(new { id = task, title = "Book physiotherapy", goalId = (Guid?)null });
		mcp.Results["goal_list"] = JsonSerializer.Serialize(new[] { new { id = goal, title = "Run a half marathon", status = 0 } });
		mcp.Results["taskitem_link_goal"] = JsonSerializer.Serialize(new { id = task, goalId = goal });
		await using var adapter = CreateAdapter(provider, mcp, mode);

		await adapter.GetResponse("Add a task to book physiotherapy", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list");
		var first = ContentsText(provider.Requests[0]);
		first.Should().Contain(TaskGoalLinkGuidance.Instructions);
		first.Should().Contain("half-marathon").And.Contain("never Paused, Achieved or Dropped");
		await adapter.GetResponse("Yes, link it", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list", "taskitem_link_goal");
		var followup = provider.Requests[mode == PlanningMode.General ? 5 : 4].GetProperty("contents").ToString();
		followup.Should().Contain("Book physiotherapy").And.Contain(task.ToString()).And.Contain(goal.ToString());
	}

	[Fact]
	public async Task ExplicitNamedGoalIsResolvedAndPassedToCreationWithoutFollowupLink()
	{
		var goal = Guid.NewGuid();
		using var provider = new ScriptedProvider(Calls(Call("goal_list", new { includeInactive = false })),
			Calls(Call("taskitem_create", new { title = "Book physiotherapy", taskListId = Guid.NewGuid(), goalId = goal })), Final("Added and linked."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["goal_list"] = JsonSerializer.Serialize(new[] { new { id = goal, title = "Run a half marathon", status = 0 } });
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await adapter.GetResponse("Add Book physiotherapy to my Run a half marathon goal", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("goal_list", "taskitem_create");
		ContentsText(provider.Requests[0]).Should().Contain("supply goalId to taskitem_create immediately");
	}

	[Fact]
	public async Task DeclineAndLaterRefreshRetainChoiceWithoutLinkMutation()
	{
		using var provider = new ScriptedProvider(Calls(Call("taskitem_create", new { title = "Book physiotherapy", taskListId = Guid.NewGuid() })),
			Calls(Call("goal_list", new { includeInactive = false })), Final("Added Book physiotherapy. Link it to Run a half marathon?"),
			Final("Okay, left unlinked."), Calls(Call("taskitem_list_by_focus_date", new { })), Final("Book physiotherapy is still open."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await adapter.GetResponse("Add Book physiotherapy", TestContext.Current.CancellationToken);
		await adapter.GetResponse("No, don't link that task", TestContext.Current.CancellationToken);
		await adapter.GetResponse("What's left today?", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list", "taskitem_list_by_focus_date");
		ContentsText(provider.Requests[^1]).Should().Contain("No, don't link that task").And.Contain("never suggest linking those same tasks again");
	}

	[Fact]
	public async Task HistorySummaryPreservesDeclinedTasksAndDistinguishesSuggestionFromConsent()
	{
		using var provider = new ScriptedProvider(Final("Declined linking Book physiotherapy to Run a half marathon."));
		await using var adapter = CreateAdapter(provider, new RecordingMcpAdapter());
		await adapter.SummarizeAsync("Assistant: Link Book physiotherapy to Run a half marathon? User: No.", TestContext.Current.CancellationToken);
		ContentsText(provider.Requests.Single()).Should().Contain(TaskGoalLinkGuidance.SummaryInstructions);
		provider.Requests.Single().TryGetProperty("tools", out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task UnrelatedOrUrgentCreationDoesNotRequireMatchingOrLinking(bool urgent)
	{
		var title = urgent ? "Call emergency plumber" : "Buy printer paper";
		var script = new List<string> { Calls(Call("taskitem_create", new { title, taskListId = Guid.NewGuid() })) };
		script.Add(Final($"Added {title}."));
		using var provider = new ScriptedProvider(script.ToArray());
		var mcp = new RecordingMcpAdapter(); mcp.Results["goal_list"] = "[]";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await adapter.GetResponse(urgent ? "Urgent, add Call emergency plumber" : "Add Buy printer paper", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create");
		ContentsText(provider.Requests[0]).Should().Contain("skip suggestions and optional goal lookups for urgent or clearly unrelated work").And.Contain("If no goal clearly matches");
	}

	[Fact]
	public async Task NoMatchingActiveGoalsDoesNotAddAQuestionOrLink()
	{
		using var provider = new ScriptedProvider(Calls(Call("taskitem_create", new { title = "Book physiotherapy", taskListId = Guid.NewGuid() })),
			Calls(Call("goal_list", new { includeInactive = false })), Final("Added Book physiotherapy."));
		var mcp = new RecordingMcpAdapter(); mcp.Results["goal_list"] = "[]";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await adapter.GetResponse("Add Book physiotherapy", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list");
		provider.Requests[^1].GetProperty("contents").ToString().Should().Contain("[]");
	}

	[Fact]
	public async Task BatchWithTwoCandidatesAndAmbiguousYesRequiresChoiceBeforeLinking()
	{
		using var provider = new ScriptedProvider(Calls(
			Call("taskitem_create", new { title = "Book physiotherapy", taskListId = Guid.NewGuid() }),
			Call("taskitem_create", new { title = "Plan recovery exercises", taskListId = Guid.NewGuid() })),
			Calls(Call("goal_list", new { includeInactive = false })), Final("Added both tasks. Link them to Run a half marathon or Improve mobility?"),
			Final("Which goal: Run a half marathon or Improve mobility?"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.WeekPlanning);
		await adapter.GetResponse("Add physiotherapy and recovery exercises", TestContext.Current.CancellationToken);
		await adapter.GetResponse("Yes", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "taskitem_create", "goal_list");
		ContentsText(provider.Requests[0]).Should().Contain("one combined suggestion for the batch").And.Contain("at most the two most likely goals").And.Contain("ask which goal");
	}

	[Theory]
	[InlineData(PlanningMode.General)]
	[InlineData(PlanningMode.GlobalPlanning)]
	public async Task DelegatedPlanLeavesGoalSuggestionsToParentAndParentCanApplyConfirmedLink(PlanningMode mode)
	{
		var task = Guid.NewGuid(); var goal = Guid.NewGuid();
		var script = new List<string>
		{
			Calls(Call("task_planning_agent_call", new { objective = "Create recovery tasks", behavior = "execution" })),
			Calls(Call("taskitem_create", new { title = "Book physiotherapy", taskListId = Guid.NewGuid() })),
			Final("{\"summary\":\"Created recovery task\",\"warnings\":[],\"unresolvedQuestions\":[]}")
		};
		if (mode == PlanningMode.General) script.Add(Calls(Call("skill_load", new { skillId = "task_management" })));
		script.Add(Calls(Call("goal_list", new { includeInactive = false })));
		script.Add(Final("Added recovery task. Link it to Run a half marathon?"));
		if (mode == PlanningMode.General) script.Add(Calls(Call("skill_load", new { skillId = "task_management" })));
		script.Add(Calls(Call("taskitem_link_goal", new { taskId = task, goalId = goal })));
		script.Add(Final("Linked recovery task."));
		using var provider = new ScriptedProvider(script.ToArray());
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_create"] = JsonSerializer.Serialize(new { id = task, title = "Book physiotherapy", goalId = (Guid?)null });
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await adapter.GetResponse("Create a recovery plan", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list");
		provider.Requests[1].GetProperty("systemInstruction").ToString().Should().Contain("set goalId only when the objective/brief explicitly authorizes").And.Contain("parent handles one combined suggestion");
		var parent = provider.Requests[mode == PlanningMode.General ? 4 : 3].GetProperty("contents").ToString();
		parent.Should().Contain(task.ToString()).And.Contain("taskitem_create");
		await adapter.GetResponse("Yes, link that task", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_create", "goal_list", "taskitem_link_goal");
	}
}
