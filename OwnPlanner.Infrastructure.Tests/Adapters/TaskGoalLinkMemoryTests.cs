using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Fact]
	public async Task LocalChoiceSchemaRequiresActionAndUsesOnlyTaskGoalChoicesOrDeclinedIds()
	{
		using var provider = new ScriptedProvider(Final("No changes"));
		await using var adapter = CreateAdapter(provider, new RecordingMcpAdapter(), PlanningMode.DayWork);
		await adapter.GetResponse("Hello", TestContext.Current.CancellationToken);
		var declaration = provider.Requests.Single().GetProperty("tools").EnumerateArray()
			.SelectMany(tool => tool.GetProperty("functionDeclarations").EnumerateArray())
			.Single(tool => tool.GetProperty("name").GetString() == "task_goal_link_choice");
		var schema = declaration.GetProperty("parameters");
		schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Should().Equal("action");
		var properties = schema.GetProperty("properties");
		properties.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("action", "choices", "taskIds");
		properties.GetProperty("action").GetProperty("enum").EnumerateArray().Select(p => p.GetString()).Should().Equal("offer", "decline");
		properties.GetProperty("choices").GetProperty("items").GetProperty("required").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo("taskId", "goalId");
	}

	[Theory]
	[InlineData(HistoryCompactionStrategy.Trim, false)]
	[InlineData(HistoryCompactionStrategy.Summarize, true)]
	[InlineData(HistoryCompactionStrategy.Summarize, false)]
	public async Task PendingAndDeclinedIdsSurviveActualCompactionEvenWhenSummaryFailsOrOmitsThem(HistoryCompactionStrategy strategy, bool summaryFails)
	{
		var ct = TestContext.Current.CancellationToken;
		var declined = Guid.NewGuid(); var pending = Guid.NewGuid(); var goal = Guid.NewGuid();
		var offer = Calls(Call("task_goal_link_choice", new { action = "offer", choices = new[] { new { taskId = declined, goalId = goal }, new { taskId = pending, goalId = goal } } }));
		var script = new List<string>
		{
			offer, Final("Link these tasks?"),
			Calls(Call("task_goal_link_choice", new { action = "decline", taskIds = new[] { declined } })), Final("Left that task unlinked."),
			Final("Another topic.")
		};
		if (strategy == HistoryCompactionStrategy.Summarize) script.Add(Final("A summary that omits all prior goal choices."));
		script.Add(Calls(Call("task_goal_link_choice", new { action = "offer", choices = new[] { new { taskId = declined, goalId = goal } } })));
		script.Add(Final("No repeat suggestion."));
		script.Add(Calls(Call("taskitem_link_goal", new { taskId = pending, goalId = goal })));
		script.Add(Final("Confirmed pending link."));
		using var provider = new ScriptedProvider(script.ToArray());
		provider.PromptTokens = count => count == 5 ? 80 : 2;
		if (summaryFails) provider.OnRequest = count => { if (count == 6) throw new InvalidOperationException("Summarizer failed"); };
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_link_goal"] = System.Text.Json.JsonSerializer.Serialize(new { id = pending, goalId = goal });
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance,
			maxContextLengthTokens: 100, compactionThresholdRatio: .7, recentTurnsToKeep: 1, compactionStrategy: strategy);
		await planning.SwitchModeAsync(PlanningMode.DayWork, ct);
		await planning.GetResponseAsync("Offer the choices", ct);
		await planning.GetResponseAsync("No thanks to the first task", ct);
		await planning.GetResponseAsync("A different topic", ct);
		await planning.GetResponseAsync("After compaction", ct);

		var after = provider.Requests.First(r => ContentsText(r).Contains("After compaction"));
		ContentsText(after).Should().NotContain("No thanks to the first task"); // older decline is gone
		var state = after.GetProperty("systemInstruction").ToString();
		state.Should().Contain("pendingChoices").And.Contain(pending.ToString()).And.Contain(goal.ToString())
			.And.Contain("declinedTaskIds").And.Contain(declined.ToString());
		provider.Requests[^1].ToString().Should().Contain("A declined task cannot be suggested again");
		mcp.Calls.Should().Equal("day_report_get", "calendar_period_get", "calendar_period_get", "calendar_period_get", "calendar_period_get"); // mode preload and fresh calendars; repeat offer never reaches MCP
		await planning.GetResponseAsync("Yes, link the remaining pending task", ct);
		var completedState = provider.Requests[^1].GetProperty("systemInstruction").ToString();
		completedState.Should().Contain(declined.ToString()).And.NotContain(pending.ToString());
	}

	[Fact]
	public async Task RecoveryAndRebuildRetainChoicesButExplicitResetAndSeparateSessionsClearThem()
	{
		var task = Guid.NewGuid(); var goal = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		using var provider = new ScriptedProvider(Calls(Call("task_goal_link_choice", new { action = "offer", choices = new[] { new { taskId = task, goalId = goal } } })),
			Final("Pending"), Final("Recovered"), Final("Rebuilt"), Final("Reset"));
		await using var adapter = CreateAdapter(provider, new RecordingMcpAdapter(), PlanningMode.DayWork);
		await adapter.GetResponse("Offer", ct);
		adapter.RecoverChatSession(); await adapter.GetResponse("After recovery", ct);
		provider.Requests[^1].GetProperty("systemInstruction").ToString().Should().Contain(task.ToString());
		var config = ModeConfig.All[PlanningMode.DayWork];
		adapter.RebuildSession(config.SystemPrompt, config.AllowedTools, []); await adapter.GetResponse("After rebuild", ct);
		provider.Requests[^1].GetProperty("systemInstruction").ToString().Should().Contain(task.ToString());
		adapter.ResetChatSession(config.SystemPrompt, config.AllowedTools); await adapter.GetResponse("After reset", ct);
		provider.Requests[^1].ToString().Should().NotContain(task.ToString());
		using var separate = new ScriptedProvider(Final("Separate session"));
		await using var other = CreateAdapter(separate, new RecordingMcpAdapter(), PlanningMode.DayWork);
		await other.GetResponse("Hello", ct); separate.Requests.Single().ToString().Should().NotContain(task.ToString());
	}

	[Fact]
	public async Task FailedPlannerLinkKeepsPendingChoiceForFreshUserDirection()
	{
		var task = Guid.NewGuid(); var goal = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		using var provider = new ScriptedProvider(Calls(Call("task_goal_link_choice", new { action = "offer", choices = new[] { new { taskId = task, goalId = goal } } })),
			Final("Link it?"), Calls(Call("taskitem_link_goal", new { taskId = task, goalId = goal })), Final("The goal was paused; choose what to do."));
		var mcp = new RecordingMcpAdapter(); mcp.Results["taskitem_link_goal"] = "{\"error\":\"Only active goals can be linked\"}";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await adapter.GetResponse("Offer", ct); await adapter.GetResponse("Yes", ct);
		provider.Requests[^1].GetProperty("systemInstruction").ToString().Should().Contain(task.ToString()).And.Contain(goal.ToString());
	}

	[Theory]
	[InlineData(PlanningMode.Reflection)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task ChoiceMemoryIsUnavailableInModesThatCannotCreateTasks(PlanningMode mode)
	{
		using var provider = new ScriptedProvider(Calls(Call("task_goal_link_choice", new { action = "offer", choices = new[] { new { taskId = Guid.NewGuid(), goalId = Guid.NewGuid() } } })), Final("Denied"));
		await using var adapter = CreateAdapter(provider, new RecordingMcpAdapter(), mode);
		await adapter.GetResponse("Offer", TestContext.Current.CancellationToken);
		Names(provider.Requests[0]).Should().NotContain("task_goal_link_choice");
		provider.Requests[^1].ToString().Should().Contain("not permitted for this model request");
	}
}
