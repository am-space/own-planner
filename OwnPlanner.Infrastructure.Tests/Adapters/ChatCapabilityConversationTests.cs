using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Theory]
	[InlineData("taskitem_create")]
	[InlineData("taskitem_update")]
	[InlineData("taskitem_assign")]
	[InlineData("taskitem_set_focus_date")]
	[InlineData("taskitem_set_important")]
	[InlineData("taskitem_complete")]
	[InlineData("taskitem_reopen")]
	[InlineData("taskitem_delete")]
	[InlineData("taskitem_list_trash")]
	[InlineData("taskitem_restore")]
	public async Task General_RequestedTaskOperation_ExecutesDirectlyAfterLoading(string operation)
	{
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "task_management" })),
			Calls(Call(operation, new { id = Guid.NewGuid(), taskListId = Guid.NewGuid(), title = "Requested change" })), Final("Confirmed"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse($"Apply the identified change: {operation}", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal(operation);
		provider.Requests.Should().HaveCount(3);
		Names(provider.Requests[0]).Should().NotContain(operation);
		Names(provider.Requests[1]).Should().Contain(operation);
	}

	[Fact]
	public async Task General_AmbiguousTarget_ReadsCandidatesAndAsksWithoutWriting()
	{
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "task_management" })),
			Calls(Call("taskitem_list_items", new { })), Final("Which Review task should I complete?"));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_list_items"] = "[{\"title\":\"Review\"},{\"title\":\"Review\"}]";
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse("Complete Review", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("taskitem_list_items");
		provider.Requests[1].GetProperty("systemInstruction").ToString().Should().Contain("Ask when the target or intended change is ambiguous");
	}

	[Fact]
	public async Task Proposal_TransmitsOnlySelectedBriefAndBlocksSpecialistWrites()
	{
		var listId = Guid.NewGuid();
		using var provider = new ScriptedProvider(
			Calls(Call("task_planning_agent_call", new { objective = "Explore Spanish study", behavior = "proposal", taskListId = listId,
				brief = new { constraints = new[] { "Two hours per week" }, userDecisions = new[] { "No weekends" }, entityReferences = new[] { new { kind = "taskList", id = listId, label = "Learning" } } } })),
			Calls(Call("taskitem_create", new { title = "Unauthorized" }), Call("search_agent_call", new { query = "Recurse" }), Call("task_planning_agent_call", new { objective = "Recurse" })),
			Final(JsonSerializer.Serialize(new { summary = "A proposal only", warnings = Array.Empty<string>(), unresolvedQuestions = Array.Empty<string>(), proposedPlan = new[] { new { description = "Try one short lesson", taskListId = listId } } })),
			Final("Here is a possible plan."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse("I'm considering Spanish. Unrelated private background must stay in the parent conversation.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("tasklist_get");
		var schema = provider.Requests[0].GetProperty("tools").ToString();
		schema.Should().Contain("behavior").And.Contain("proposal").And.Contain("execution").And.Contain("brief").And.Contain("entityReferences");
		Names(provider.Requests[1]).Should().OnlyContain(name => TaskPlanningMcpAdapter.ReadTools.Contains(name));
		var specialistContext = provider.Requests[1].GetProperty("contents").ToString();
		specialistContext.Should().Contain("Two hours per week").And.Contain("No weekends").And.Contain(listId.ToString()).And.NotContain("Unrelated private background");
		provider.Requests[2].ToString().Should().Contain("not allowed");
		var parentResult = provider.Requests[3].ToString();
		parentResult.Should().Contain("proposedPlan").And.Contain("Try one short lesson").And.Contain("proposed");
	}

	[Fact]
	public async Task InvalidBriefOrBehavior_FailsBeforeStartingSpecialistOrAccessingScope()
	{
		using var provider = new ScriptedProvider(Calls(
			Call("task_planning_agent_call", new { objective = "Plan", behavior = "invalid", contextId = Guid.NewGuid() }),
			Call("task_planning_agent_call", new { objective = "Plan", brief = new { constraints = new[] { new string('x', 301) } }, contextId = Guid.NewGuid() })), Final("Please narrow the brief"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse("Plan", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		provider.Requests.Should().HaveCount(2);
		provider.Requests[1].ToString().Should().Contain("Invalid task-planning request");
	}

	[Theory]
	[InlineData(PlanningMode.WeekPlanning, "weekly_planning", "taskitem_set_focus_date", "goal_update")]
	[InlineData(PlanningMode.Reflection, "reflection", "noteitem_create", "taskitem_create")]
	[InlineData(PlanningMode.SystemAnalysis, "strategic_review", "strategic_report_get", "taskitem_update")]
	[InlineData(PlanningMode.GlobalPlanning, "goals_organization", "goal_update", "taskitem_reopen")]
	public async Task SpecializedWorkflow_ReappliesBaselineInstructionsAndPreservesExecutionCeiling(
		PlanningMode mode, string skill, string allowed, string denied)
	{
		using var provider = new ScriptedProvider(Calls(Call(allowed, new { }), Call(denied, new { })), Final("Done"), Final("Next request"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await adapter.GetResponse("Perform the requested review or edit", TestContext.Current.CancellationToken);
		await adapter.GetResponse("Continue", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal(allowed);
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
		foreach (var request in provider.Requests)
		{
			Names(request).Should().BeEquivalentTo(ModeConfig.All[mode].AllowedTools);
			request.GetProperty("systemInstruction").ToString().Should().Contain(ChatSkillRegistry.All[skill].Instructions);
		}
	}

	[Fact]
	public async Task ProviderFailureAfterTaskSkillLoad_DiscardsActivationOnRecovery()
	{
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "task_management" })), Final("Failed"), Final("Recovered"));
		provider.OnRequest = count => { if (count == 2) throw new InvalidOperationException("Provider failed"); };
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		var act = () => adapter.GetResponse("Load tasks", TestContext.Current.CancellationToken);
		await act.Should().ThrowAsync<Mscc.GenerativeAI.Types.GeminiApiException>();
		provider.OnRequest = null;
		adapter.RecoverChatSession();
		await adapter.GetResponse("Continue", TestContext.Current.CancellationToken);
		Names(provider.Requests[^1]).Should().NotContain("taskitem_update");
		mcp.Calls.Should().BeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ScriptedMeasurement_RepeatedRoutineEditsAndComplexPlanning(bool complex)
	{
		// Run unchanged on the pre-feature tree and the implementation. Fake responses measure
		// orchestration only; they do not evaluate model routing, network or Gemini latency.
		var direct = !complex && ChatSkillRegistry.All.ContainsKey("task_management");
		var times = new List<double>();
		(int Requests, int Loads, int Agents) measured = default;
		for (var sample = 0; sample < 12; sample++)
		{
			var script = new List<string>();
			for (var turn = 0; turn < 3; turn++)
			{
				script.Add(Calls(direct ? Call("skill_load", new { skillId = "task_management" })
					: Call("task_planning_agent_call", new { objective = complex ? "Create and schedule a three-step launch plan" : "Rename the identified task" })));
				script.Add(Calls(Call("taskitem_update", new { id = Guid.NewGuid(), title = "Revised" })));
				if (complex) script.Add(Calls(Call("taskitem_create", new { taskListId = Guid.NewGuid(), title = "Next step" })));
				if (!direct) script.Add(Final("{\"summary\":\"Changes completed\",\"warnings\":[],\"unresolvedQuestions\":[]}"));
				script.Add(Final("Done"));
			}
			using var provider = new ScriptedProvider(script.ToArray());
			var mcp = new RecordingMcpAdapter();
			await using var adapter = CreateAdapter(provider, mcp);
			var timer = Stopwatch.StartNew();
			for (var turn = 0; turn < 3; turn++)
				await adapter.GetResponse(complex ? "Create and schedule the launch plan" : "Rename that task to Revised", TestContext.Current.CancellationToken);
			timer.Stop();
			if (sample > 0) times.Add(timer.Elapsed.TotalMilliseconds);
			provider.Requests.Should().HaveCount(complex ? 15 : direct ? 9 : 12);
			mcp.Calls.Should().HaveCount(complex ? 6 : 3);
			measured = (provider.Requests.Count, provider.EmittedToolCalls.Count(name => name == "skill_load"), provider.EmittedToolCalls.Count(name => name == "task_planning_agent_call"));
			measured.Loads.Should().Be(direct ? 3 : 0);
			measured.Agents.Should().Be(direct ? 0 : 3);
		}
		times.Sort();
		TestContext.Current.TestOutputHelper!.WriteLine($"Scripted {(complex ? "complex" : "routine")}: 3 turns, requests={measured.Requests}, skill loads={measured.Loads}, agent calls={measured.Agents}, median={times[times.Count / 2]:F3}ms, range={times[0]:F3}–{times[^1]:F3}ms (11 samples after warmup).");
	}
}
