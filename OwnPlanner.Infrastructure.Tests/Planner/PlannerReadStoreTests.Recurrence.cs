using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Planner;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Planner;

namespace OwnPlanner.Infrastructure.Tests.Planner;

public partial class PlannerReadStoreTests
{
	[Theory]
	[InlineData("retain", true)]
	[InlineData("skipCurrent", true)]
	[InlineData("skipExpired", false)]
	[InlineData("completed", false)]
	[InlineData("cancelled", false)]
	[InlineData("skipped", false)]
	[InlineData("closed", false)]
	[InlineData("trashed", false)]
	[InlineData("archived", false)]
	public async Task RecurrenceMetadata_PagesStoredSeriesAndFlagsOnlyActionablePreviousWork(string scenario, bool outstanding)
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var oldList = new TaskList("Old private list");
		var nextList = new TaskList("Current list");
		var old = new TaskItem("Earlier series", oldList.Id, new string('x', 1000));
		var policy = scenario.StartsWith("skip", StringComparison.Ordinal)
			? MissedOccurrencePolicy.SkipExpiredPeriods : MissedOccurrencePolicy.RetainEachObligation;
		old.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			new(2026, 11, 13), null, policy));
		var next = old.CreateRecurrenceSuccessor(new(RecurrenceRule.Monthly(2, [29, 30, 31]),
			new(2026, 11, 16), new(2027, 3, 31), policy));
		next.SetTitle("Current series"); next.AssignToList(nextList.Id);
		if (scenario == "closed") old.Complete();
		if (scenario == "trashed") old.Trash();
		if (scenario == "archived") oldList.Archive();
		db.AddRange(oldList, nextList, old, next, new TaskItem("Ordinary", nextList.Id));
		if (scenario is "completed" or "cancelled" or "skipped") db.TaskOccurrenceFacts.Add(new()
		{
			TaskId = old.Id, OccurrenceDate = new(2026, 11, 13),
			Status = scenario == "completed" ? TaskOccurrenceStatus.Completed : scenario == "cancelled" ? TaskOccurrenceStatus.Cancelled : TaskOccurrenceStatus.Skipped,
			CompletedAt = scenario == "completed" ? new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc) : null
		});
		await db.SaveChangesAsync(ct);
		var now = new DateTime(2026, 11, scenario == "skipExpired" ? 16 : 15, 18, 0, 0, DateTimeKind.Utc);
		var calendar = Substitute.For<IPlanningCalendar>();
		calendar.ResolveAsync("today", Arg.Any<CancellationToken>()).Returns(PlanningCalendarResolver.Resolve("today", now, "UTC"));
		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection), calendar, new RecurrenceCalculator());
		var before = await db.TaskOccurrenceFacts.CountAsync(ct);
		var page = await store.QueryTasksAsync(new(Search: "Current", TaskListId: nextList.Id, Limit: 1), ct);
		page.TotalCount.Should().Be(1); page.Items.Should().ContainSingle();
		var metadata = page.Items[0].Recurrence!;
		metadata.Summary.Should().Be("Every 2 months on days 29, 30, 31 (clamped to month end)");
		metadata.StartDate.Should().Be(new DateOnly(2026, 11, 16)); metadata.EndDate.Should().Be(new DateOnly(2027, 3, 31));
		metadata.MissedPolicy.Should().Be(policy.ToString()); metadata.PredecessorTaskId.Should().Be(old.Id);
		metadata.PreviousSeriesHasOutstandingWork.Should().Be(outstanding); metadata.HasOutstandingWork.Should().BeFalse();
		(await store.GetTaskAsync(next.Id, ct))!.Recurrence.Should().Be(metadata);
		var previous = await store.GetTaskAsync(old.Id, ct);
		if (scenario == "trashed") previous.Should().BeNull();
		else
		{
			previous!.Recurrence!.HasOutstandingWork.Should().Be(outstanding);
			previous.Recurrence.SuccessorTaskId.Should().Be(next.Id);
			previous.Recurrence.Summary.Should().Contain("Friday").And.Contain("week starts Monday");
		}
		var all = await store.QueryTasksAsync(new(Status: PlannerTaskStatus.All, Limit: 1), ct);
		all.TotalCount.Should().Be(scenario == "trashed" ? 2 : 3); all.Items.Should().ContainSingle();
		var ordinary = (await store.QueryTasksAsync(new(Search: "Ordinary"), ct)).Items.Single();
		ordinary.Recurrence.Should().BeNull();
		(await db.TaskOccurrenceFacts.CountAsync(ct)).Should().Be(before); // reads never reconcile
	}
}
