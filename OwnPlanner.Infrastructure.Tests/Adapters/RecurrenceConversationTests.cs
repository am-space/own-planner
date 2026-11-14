using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Theory]
	[InlineData("proposal")]
	[InlineData("execution")]
	public async Task DelegatedRecurrenceCreationRespectsBehaviorAndReturnsConfirmedActionsOnly(string behavior)
	{
		var list = Guid.NewGuid();
		using var provider = new ScriptedProvider(
			Calls(Call("task_planning_agent_call", new { objective = "Create weekly Friday walks with retain each obligation policy", behavior, taskListId = list })),
			Calls(Call("recurring_task_create", new { title = "Walk", taskListId = list, startDate = "2026-11-14", frequency = "Weekly", weekdays = new[] { "Friday" }, missedPolicy = "RetainEachObligation" })),
			Final("{\"summary\":\"Reviewed recurring work\",\"warnings\":[],\"unresolvedQuestions\":[],\"proposedPlan\":[]}"), Final("Done"));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = list });
		mcp.Results["recurring_task_create"] = JsonSerializer.Serialize(new { task = new { id = Guid.NewGuid(), taskListId = list } });
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse("Plan the requested recurring work", TestContext.Current.CancellationToken);
		if (behavior == "proposal")
		{
			mcp.Calls.Should().Equal("tasklist_get");
			Names(provider.Requests[1]).Should().NotContain("recurring_task_create", "recurring_task_successor_apply");
			provider.Requests[2].ToString().Should().Contain("not allowed");
		}
		else
		{
			mcp.Calls.Should().Equal("tasklist_get", "recurring_task_create");
			Names(provider.Requests[1]).Should().Contain("recurring_task_create");
			provider.Requests[3].ToString().Should().Contain("recurring_task_create").And.Contain("actions");
		}
		provider.Requests[1].GetProperty("systemInstruction").ToString().Should().Contain("Only the user chooses").And.Contain("explicit start date");
	}

	[Fact]
	public async Task RecurrenceLoadsForAuthorizedCreationThenRefreshesCompositeWork()
	{
		var task = Guid.NewGuid(); var list = Guid.NewGuid();
		using var provider = new ScriptedProvider(
			Calls(Call("skill_load", new { skillId = "task_management" })),
			Calls(Call("recurring_task_create", new { title = "Walk", taskListId = list, startDate = "2026-11-14", frequency = "Weekly", weekdays = new[] { "Friday" }, missedPolicy = "RetainEachObligation" })),
			Calls(Call("planning_work_query", new { startDate = "2026-11-14", endExclusiveDate = "2026-11-28", taskId = task })), Final("Created the series; both periods are visible."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["recurring_task_create"] = JsonSerializer.Serialize(new { task = new { id = task } });
		mcp.Results["planning_work_query"] = JsonSerializer.Serialize(new { items = new[] { new { taskId = task, occurrenceDate = "2026-11-20" }, new { taskId = task, occurrenceDate = "2026-11-27" } }, totalCount = 2 });
		await using var adapter = CreateAdapter(provider, mcp);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		await planning.GetResponseAsync("Create weekly Friday walks, retaining every missed obligation, and show the coming two periods.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("general_report_get", "calendar_period_get", "recurring_task_create", "planning_work_query");
		Names(provider.Requests[0]).Should().NotContain("recurring_task_create");
		Names(provider.Requests[1]).Should().Contain("recurring_task_create");
		provider.Requests[^1].ToString().Should().Contain("2026-11-20").And.Contain("2026-11-27");
		ContentsText(provider.Requests[0]).Should().Contain("Ask which").And.Contain("never invent a completion timestamp").And.Contain("Only the user chooses");
	}

	[Fact]
	public async Task DayCompletionUsesCompositeIdentityAndFreshReportWithoutParentCompletion()
	{
		var task = Guid.NewGuid();
		using var provider = new ScriptedProvider(Calls(Call("task_occurrence_complete", new { taskId = task, occurrenceDate = "2026-11-13", completedAt = "2026-11-14T09:00:00Z" })),
			Calls(Call("day_report_get", new { })), Final("Completed Friday's occurrence; the series continues."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		await planning.SwitchModeAsync(PlanningMode.DayWork, TestContext.Current.CancellationToken);
		await planning.GetResponseAsync("I completed Friday's walk today at 09:00 UTC.", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("day_report_get", "calendar_period_get", "task_occurrence_complete", "day_report_get");
		mcp.Calls.Should().NotContain("taskitem_complete");
		Names(provider.Requests[0]).Should().Contain("task_occurrence_complete").And.NotContain("recurring_task_create", "recurring_task_successor_apply");
		ContentsText(provider.Requests[0]).Should().Contain("Ask which occurrence when ambiguous");
	}

	[Theory]
	[InlineData(PlanningMode.Reflection)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task ReadModesRejectRecurrenceMutationsEvenIfProviderRequestsThem(PlanningMode mode)
	{
		using var provider = new ScriptedProvider(Calls(Call("task_occurrence_complete", new { taskId = Guid.NewGuid(), occurrenceDate = "2026-11-13" }),
			Call("recurring_task_create", new { title = "Unauthorized" }), Call("recurring_task_successor_apply", new { })), Final("Those changes are unavailable in this mode."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await adapter.GetResponse("Update recurring work", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		Names(provider.Requests[0]).Should().Contain("planning_work_query").And.NotContain("task_occurrence_complete", "recurring_task_create");
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
	}

	[Theory]
	[InlineData("Create a recurring task", "Should missed periods be skipped, or should every obligation remain?")]
	[InlineData("I did last Friday's occurrence earlier", "When did you complete it, and in which timezone?")]
	public async Task MissingPolicyOrAmbiguousActualTimeCanBeClarifiedWithoutWrites(string prompt, string question)
	{
		using var provider = new ScriptedProvider(Final(question)); var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var result = await planning.GetResponseAsync(prompt, TestContext.Current.CancellationToken);
		result.Message.Should().Be(question);
		mcp.Calls.Should().Equal("general_report_get", "calendar_period_get");
		ContentsText(provider.Requests[0]).Should().Contain("policy the user wants when missing").And.Contain("Ask about ambiguous local/DST times");
	}
}
