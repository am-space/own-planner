using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	// These scripts verify policy/context delivery and routing, not a live model's capacity judgments.
	[Theory]
	[InlineData("Five short tasks, about ten minutes each. I have two hours available this week.")]
	[InlineData("Five large tasks, about four hours each. I have two hours available this week.")]
	[InlineData("There are five tasks, but I have not estimated their sizes or my available time.")]
	public async Task WeeklyCapacityDiscussionCarriesPriorConstraintsAndExactCountEvidenceWithoutWrites(string constraints)
	{
		using var provider = new ScriptedProvider(Final("I will keep those constraints in mind."),
			Calls(Call("weekly_report_get", new { calendarPeriod = "thisWeek" })), Final("Here is a proposal to review."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["weekly_report_get"] = WeeklyCountEvidence();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.WeekPlanning);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.SwitchModeAsync(PlanningMode.WeekPlanning, ct);
		await planning.GetResponseAsync(constraints, ct);
		await planning.GetResponseAsync("What's realistic to get done this week?", ct);

		mcp.Calls.Should().Equal("weekly_report_get", "calendar_period_get", "calendar_period_get", "weekly_report_get");
		mcp.Arguments.Where(call => call.Tool == "weekly_report_get").Should()
			.OnlyContain(call => call.Values!["calendarPeriod"]!.ToString() == "thisWeek");
		var context = ContentsText(provider.Requests[1]);
		context.Should().Contain(constraints).And.Contain("What's realistic to get done this week?");
		var instructions = provider.Requests[1].GetProperty("systemInstruction").ToString();
		instructions.Should().Contain("not proof of overload or feasibility")
			.And.Contain("constraints already supplied in the conversation")
			.And.Contain("ask briefly about available time and the largest tasks")
			.And.Contain("small core set of tasks and optional reserve work");
		provider.Requests[2].ToString().Should().Contain("distinctTaskCount").And.Contain("overloadedDayThreshold");
	}

	[Fact]
	public async Task GeneralLoadsTheSameWeeklyGuidanceForANamedPeriodWithoutActivatingTaskWrites()
	{
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "weekly_planning" })),
			Calls(Call("weekly_report_get", new { calendarPeriod = "nextWeek" })), Final("Here is a proposed core and reserve."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["weekly_report_get"] = WeeklyCountEvidence();
		await using var adapter = CreateAdapter(provider, mcp);
		await adapter.GetResponse("Help me decide what is realistic next week; don't schedule anything yet.", TestContext.Current.CancellationToken);

		mcp.Calls.Should().Equal("weekly_report_get");
		Names(provider.Requests[0]).Should().NotContain("weekly_report_get");
		Names(provider.Requests[1]).Should().Contain("weekly_report_get").And.NotContain("taskitem_set_focus_date", "taskitem_update");
		provider.Requests[1].GetProperty("systemInstruction").ToString().Should()
			.Contain("not proof of overload or feasibility")
			.And.Contain("A capacity discussion or proposed plan does not authorize writes")
			.And.Contain("never manufacture deadlines to fill a schedule");
		mcp.Arguments.Single().Values!["calendarPeriod"]!.ToString().Should().Be("nextWeek");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcreteSchedulingAfterExplorationRoutesFocusOrAGenuineDeadlineWithoutRequestingOtherDateChanges(bool genuineDeadline)
	{
		var task = Guid.NewGuid();
		var list = Guid.NewGuid();
		var deadline = "2026-11-20T17:00:00Z";
		var focus = "2026-11-16T00:00:00Z";
		var write = genuineDeadline ? "taskitem_update" : "taskitem_set_focus_date";
		var arguments = genuineDeadline ? (object)new { id = task, dueAt = deadline } : new { id = task, focusDate = "2026-11-16" };
		using var provider = new ScriptedProvider(Final("We can consider that task as core work; this is a proposal."),
			Calls(Call("taskitem_get", new { id = task }), Call("tasklist_get", new { id = list })),
			Calls(Call(write, arguments)), Calls(Call("weekly_report_get", new { calendarPeriod = "thisWeek" })), Final("The requested change is confirmed."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["taskitem_get"] = JsonSerializer.Serialize(new { id = task, taskListId = list, isCompleted = false, dueAt = deadline, focusAt = focus });
		mcp.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = list, isArchived = false });
		mcp.Results[write] = genuineDeadline ? JsonSerializer.Serialize(new { id = task, dueAt = deadline, focusAt = focus })
			: JsonSerializer.Serialize(new { success = true, id = task, focusDate = focus });
		mcp.Results["weekly_report_get"] = WeeklyCountEvidence();
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.WeekPlanning);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.SwitchModeAsync(PlanningMode.WeekPlanning, ct);
		await planning.GetResponseAsync("Could this task fit into the week? I haven't decided to schedule it.", ct);
		mcp.Calls.Should().Equal("weekly_report_get", "calendar_period_get");

		await planning.GetResponseAsync(genuineDeadline
			? $"The client deadline for task {task} is November 20 at 17:00 UTC. Set that deadline."
			: $"Schedule task {task} for November 16 as planned work, preserving its existing deadline.", ct);

		mcp.Calls.Should().Equal("weekly_report_get", "calendar_period_get", "calendar_period_get", "taskitem_get", "tasklist_get", write, "weekly_report_get");
		var written = mcp.Arguments.Single(call => call.Tool == write).Values!;
		written.Keys.Should().BeEquivalentTo(genuineDeadline ? ["id", "dueAt"] : ["id", "focusDate"]);
		written[genuineDeadline ? "dueAt" : "focusDate"]!.ToString().Should().Be(genuineDeadline ? deadline : "2026-11-16");
		provider.Requests[3].ToString().Should().Contain(genuineDeadline ? deadline : "success").And.Contain(focus);
		provider.Requests[1].GetProperty("systemInstruction").ToString().Should()
			.Contain("Scheduling work without a real deadline changes only its focus date")
			.And.Contain("Create or change DueAt only for genuine deadlines supported by user intent")
			.And.Contain("Execute concrete requested changes without redundant confirmation");
	}

	private static string WeeklyCountEvidence()
	{
		var date = new DateOnly(2026, 11, 16);
		var report = new WeeklyReport(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), date, date.AddDays(7),
			"UTC", "[windowStartDate, windowEndExclusiveDate)", 5,
			new WeeklyOverallTotals(5, 0, 5, 0, 0, 0),
			Enumerable.Range(0, 7).Select(offset => new WeeklyDaySummary(date.AddDays(offset),
				offset == 0 ? 5 : 0, 0, offset == 0 ? 5 : 0, offset == 0, [], [])).ToArray(), [], [],
			new WeeklyPlanningSignals([new WeeklyOverloadedDay(date, 5)], [], [], 0, [], 0));
		return JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}
}
