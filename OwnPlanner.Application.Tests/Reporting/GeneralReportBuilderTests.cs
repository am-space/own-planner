using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;

namespace OwnPlanner.Application.Tests.Reporting;

public sealed class GeneralReportBuilderTests
{
	private static readonly DateTime Today = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
	private static GeneralTaskRow Row(int id, DateTime? due = null, DateTime? focus = null, bool completed = false, Guid? goal = null, string title = "Task") =>
		new(new Guid(id, 0, 0, new byte[8]), title, completed, focus, due, WellKnownIds.InboxTaskList, goal);

	[Fact]
	public void Build_DisjointCalendarDeadlinesAndFocusCounts_AtYearBoundary()
	{
		var tasks = new[] {
			Row(1, Today.AddTicks(-1)), Row(2, Today), Row(3, Today.AddDays(1).AddTicks(-1)),
			Row(4, Today.AddDays(1)), Row(5, Today.AddDays(8).AddTicks(-1)), Row(6, Today.AddDays(8)),
			Row(7, Today, Today, completed: true), Row(8, focus: Today.AddTicks(-1)),
			Row(9, focus: Today), Row(10, focus: Today.AddDays(1).AddTicks(-1)), Row(11, focus: Today.AddDays(1)), Row(12)
		};
		var report = GeneralReportBuilder.Build(Today.AddHours(12), tasks, [], 4);
		report.TimeZone.Should().Be("UTC");
		report.UpcomingStartDate.Should().Be(new DateOnly(2027, 1, 1));
		report.UpcomingEndExclusiveDate.Should().Be(new DateOnly(2027, 1, 8));
		report.Commitments.OverdueCount.Should().Be(1);
		report.Commitments.DueTodayCount.Should().Be(2, "earlier today stays in due-today rather than overlapping overdue");
		report.Commitments.UpcomingSevenDayCount.Should().Be(2);
		report.Commitments.Nearest.TotalCount.Should().Be(5);
		report.Today.FocusTaskCount.Should().Be(3);
		report.Today.CompletedCount.Should().Be(1);
		report.Today.Remaining.TotalCount.Should().Be(2);
		report.Inbox.Should().Be(new GeneralInbox(4, 1));
	}

	[Fact]
	public void Build_DeduplicatesBoundsAndOrdersSamples_WithoutChangingExactCounts()
	{
		var goals = Enumerable.Range(1, 5).Select(i => new GeneralGoalRow(new Guid(i, 0, 0, new byte[8]), new string('g', 100))).ToArray();
		var tasks = Enumerable.Range(1, 20).Select(i => Row(i, Today, Today, goal: goals[i % 5].Id, title: new string('t', 100))).ToArray();
		var report = GeneralReportBuilder.Build(Today, tasks.Reverse().ToArray(), goals, 0);
		report.Should().BeEquivalentTo(GeneralReportBuilder.Build(Today, tasks, goals.Reverse().ToArray(), 0), o => o.WithStrictOrdering());
		report.Today.Remaining.TotalCount.Should().Be(20);
		report.Today.Remaining.Truncated.Should().BeTrue();
		report.Today.Remaining.TaskIds.Should().Equal(tasks.Take(5).Select(t => t.Id));
		report.Commitments.Nearest.TaskIds.Should().Equal(tasks.Take(3).Select(t => t.Id));
		report.Commitments.Nearest.Truncated.Should().BeTrue();
		report.Tasks.Should().HaveCount(5).And.OnlyContain(t => t.Title.Length == 80 && t.TitleTruncated);
		report.Direction.ActiveGoalCount.Should().Be(5);
		report.Direction.LinkedGoalCount.Should().Be(5);
		report.Direction.Truncated.Should().BeTrue();
		report.Direction.Goals.Should().HaveCount(3).And.OnlyContain(g => g.Title.Length == 80 && g.TitleTruncated);
	}

	[Fact]
	public void Build_DirectionIncludesTodayFocusAndFutureCommitments_NotUnlinkedOrOnlyOverdue()
	{
		var goals = Enumerable.Range(1, 5).Select(i => new GeneralGoalRow(new Guid(i, 0, 0, new byte[8]), $"Goal {i}")).ToArray();
		var report = GeneralReportBuilder.Build(Today, [Row(1, focus: Today, completed: true, goal: goals[0].Id),
			Row(2, Today.AddDays(7), goal: goals[1].Id), Row(3, Today.AddDays(-1), goal: goals[2].Id),
			Row(4, Today.AddDays(8), goal: goals[3].Id)], goals, 0);
		report.Direction.ActiveGoalCount.Should().Be(5);
		report.Direction.Goals.Select(g => g.Id).Should().Equal(goals.Take(2).Select(g => g.Id));
	}

	[Fact]
	public void Build_RepresentativeFixtureIsCompact_AndEmptyDataHasNoSamples()
	{
		var goals = new[] { new GeneralGoalRow(Guid.NewGuid(), "Learn Spanish"), new GeneralGoalRow(Guid.NewGuid(), "Practice conversation"), new GeneralGoalRow(Guid.NewGuid(), "Plan a trip") };
		var tasks = Enumerable.Range(1, 8).Select(i => Row(i, i > 5 ? Today.AddDays(i - 6) : null, i <= 5 ? Today : null, goal: goals[i % 3].Id, title: $"Practice lesson {i} and review vocabulary")).ToArray();
		var report = GeneralReportBuilder.Build(Today, tasks, goals, 2);
		var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		// Character/4 is a rough fixture-size diagnostic, not a tokenizer or universal guarantee.
		(json.Length / 4d).Should().BeInRange(600, 1000);
		var empty = GeneralReportBuilder.Build(Today, [], [], 0);
		empty.Today.FocusTaskCount.Should().Be(0);
		empty.Tasks.Should().BeEmpty();
		empty.Direction.Goals.Should().BeEmpty();
		empty.Today.Remaining.Truncated.Should().BeFalse();
		empty.Commitments.Nearest.Truncated.Should().BeFalse();
	}
}
