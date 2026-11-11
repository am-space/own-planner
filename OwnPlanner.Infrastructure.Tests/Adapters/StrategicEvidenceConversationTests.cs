using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	// Scripted responses verify delivered evidence, policy and routing, not live-model diagnoses.
	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task StrategicReviewCarriesBoundedEvidenceAndRoutesFreshTargetedReads(PlanningMode mode)
	{
		var goal = Guid.NewGuid();
		var context = Guid.NewGuid();
		var task = Guid.NewGuid();
		var note = Guid.NewGuid();
		using var provider = new ScriptedProvider(
			Calls(Call("taskitem_get", new { id = task }), Call("noteitem_get", new { id = note }), Call("goal_get", new { id = goal })),
			Calls(Call("taskitem_list_items", new { includeCompleted = true, limit = 1, offset = 0 }), Call("context_get", new { id = context })),
			Calls(Call("strategic_report_get", new { taskSampleLimit = 0, noteSampleLimit = 0 })),
			Final("The structure is worth reviewing; the samples alone do not establish a problem."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["strategic_report_get"] = StrategicEvidence(goal, context, task, note);
		mcp.Results["taskitem_get"] = JsonSerializer.Serialize(new { id = task, title = "Buy groceries", description = "Routine household operation; deliberately no goal.", goalId = (Guid?)null });
		mcp.Results["noteitem_get"] = JsonSerializer.Serialize(new { id = note, content = "This context is intentionally kept for reference notes." });
		mcp.Results["goal_get"] = JsonSerializer.Serialize(new { id = goal, title = "Delivered project", status = "Active" });
		mcp.Results["taskitem_list_items"] = JsonSerializer.Serialize(new
		{
			items = new[] { new { id = Guid.NewGuid(), goalId = goal, isCompleted = true } }, totalCount = 28, offset = 0, limit = 1, hasMore = true
		});
		mcp.Results["context_get"] = JsonSerializer.Serialize(new { id = context, name = "Reference", description = "Intentionally notes only" });
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.SwitchModeAsync(mode, ct);
		// Refresh returns a later dated snapshot while retaining the same structural observations.
		mcp.Results["strategic_report_get"] = StrategicEvidence(goal, context, task, note, refreshed: true);
		await planning.GetResponseAsync("Review current structure. Check material details before diagnosing; don't change anything.", ct);

		mcp.Calls.Should().Equal("strategic_report_get", "taskitem_get", "noteitem_get", "goal_get",
			"taskitem_list_items", "context_get", "strategic_report_get");
		var initialEvidence = ContentsText(provider.Requests[0]);
		initialEvidence.Should().Contain("descriptionTruncated").And.Contain("contentTruncated")
			.And.Contain("incompleteTaskCount").And.Contain("tasksWithoutGoalCount").And.Contain("2026-11-10");
		var details = provider.Requests[2].GetProperty("contents").ToString();
		details.Should().Contain("deliberately no goal").And.Contain("reference notes")
			.And.Contain("isCompleted").And.Contain("hasMore").And.Contain("28");
		provider.Requests[3].GetProperty("contents").ToString().Should().Contain("2026-11-11");
		mcp.Arguments.Single(call => call.Tool == "taskitem_list_items").Values!["includeCompleted"]!.ToString().Should().Be("True");
		foreach (var request in provider.Requests)
		{
			Names(request).Should().BeEquivalentTo(ModeConfig.All[mode].AllowedTools);
			SystemInstructionText(request).Should().Contain(ChatSkillRegistry.All["strategic_review"].Instructions);
		}
	}

	[Theory]
	[InlineData("Achieved")]
	[InlineData("Paused")]
	public async Task GlobalPlanningProposalsDoNotWriteUntilConcreteGoalChangeIsRequested(string status)
	{
		var goal = Guid.NewGuid();
		using var provider = new ScriptedProvider(Final("We can review a next step, completion or pause; these are proposals."),
			Calls(Call("goal_get", new { id = goal })), Calls(Call("goal_update", new { id = goal, status })), Final("The requested status is confirmed."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["goal_get"] = JsonSerializer.Serialize(new { id = goal, status = "Active" });
		mcp.Results["goal_update"] = JsonSerializer.Serialize(new { id = goal, status });
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.GlobalPlanning);
		await adapter.GetResponse("Could this goal be finished or should I pause it? I am still considering options.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		await adapter.GetResponse($"Set goal {goal} status to {status}.", TestContext.Current.CancellationToken);

		mcp.Calls.Should().Equal("goal_get", "goal_update");
		mcp.Arguments.Single(call => call.Tool == "goal_update").Values!["status"]!.ToString().Should().Be(status);
		provider.Requests[^1].GetProperty("contents").ToString().Should().Contain("functionResponse").And.Contain(status);
		SystemInstructionText(provider.Requests[1]).Should().Contain("without redundant confirmation")
			.And.Contain("Exploratory analysis and proposals do not authorize writes");
	}

	[Fact]
	public async Task SystemAnalysisRejectsWritesDelegationAndSkillEscalationEvenWhenRequested()
	{
		using var provider = new ScriptedProvider(Calls(Call("goal_update", new { id = Guid.NewGuid(), status = "Paused" }),
			Call("task_planning_agent_call", new { objective = "Fix everything", behavior = "execution" }),
			Call("skill_load", new { skillId = "goals_organization" })), Final("This mode is read-only; review the suggestions elsewhere."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.SystemAnalysis);
		await adapter.GetResponse("Pause that goal and fix all structural issues.", TestContext.Current.CancellationToken);

		mcp.Calls.Should().BeEmpty();
		Names(provider.Requests[0]).Should().NotContain("goal_update", "task_planning_agent_call", "skill_load");
		provider.Requests[1].GetProperty("contents").ToString().Should().Contain("not permitted for this model request");
		ContentsText(provider.Requests[0]).Should().Contain("Do not make any changes, offer to execute fixes or switch modes automatically");
	}

	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task EmptyStrategicSnapshotSupportsNoSignificantIssuesWithoutAdditionalReadsOrWrites(PlanningMode mode)
	{
		using var provider = new ScriptedProvider(Final("No significant issues detected in the available data."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["strategic_report_get"] = JsonSerializer.Serialize(new StrategicReport(DateTime.UnixEpoch,
			new StrategicOverallTotals(0, 0, 0, 0, 0, 0, 0, 0), [], [], new StrategicStructuralSignals([], [], [], 0, [])),
			new JsonSerializerOptions(JsonSerializerDefaults.Web));
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		await planning.SwitchModeAsync(mode, TestContext.Current.CancellationToken);
		await planning.GetResponseAsync("Anything significant to review?", TestContext.Current.CancellationToken);

		mcp.Calls.Should().Equal("strategic_report_get");
		ContentsText(provider.Requests[0]).Should().Contain("activeGoalCount").And.Contain("contextsWithoutActiveTasks");
		SystemInstructionText(provider.Requests[0]).Should().Contain("Do not manufacture findings");
	}

	private static string SystemInstructionText(JsonElement request) => string.Join("\n",
		request.GetProperty("systemInstruction").GetProperty("parts").EnumerateArray().Select(part => part.GetProperty("text").GetString()));

	private static string StrategicEvidence(Guid goal, Guid context, Guid task, Guid note, bool refreshed = false)
	{
		var list = Guid.NewGuid();
		var report = new StrategicReport(new DateTime(2026, 11, refreshed ? 11 : 10, 12, 0, 0, DateTimeKind.Utc),
			new StrategicOverallTotals(1, 1, 1, 1, 7, 0, 0, 3),
			[new StrategicContextSummary(context, "Reference", ContextType.Area, ContextStatus.Active,
				0, 1, 0, 0, 0, 0, 3, [], refreshed ? [] : [new StrategicNoteSample(note, "Reference", "Partial preview", true, false, DateTime.UnixEpoch, list, context, null)])],
			[new StrategicGoalSummary(goal, "Delivered project", GoalHorizon.Yearly, "2026", null, null, null, 0, 0, 0, 0, 0, 0, [], [])],
			new StrategicStructuralSignals([new(goal, "Delivered project")], [new(context, "Reference")],
				refreshed ? [] : [new StrategicTaskSample(task, "Buy groceries", "Partial preview", true, false, null, null, list, null, null)], 7, []));
		return JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}
}
