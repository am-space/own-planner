using FluentAssertions;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;

namespace OwnPlanner.Application.Tests.Reporting;

public sealed class DayReportBuilderTests
{
	private static DateTime Utc(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
	private static GeneralTaskRow Row(int id, string? focus = null, string? due = null, bool completed = false) =>
		new(new Guid(id, 0, 0, new byte[8]), "Task", completed, focus is null ? null : Utc(focus), due is null ? null : Utc(due), WellKnownIds.InboxTaskList, null);
	private static PlanningPeriod Today => PlanningCalendarResolver.Resolve("today", Utc("2026-10-02T12:00Z"), "UTC");

	[Fact]
	public void DailyOverviewIncludesDueOnlyDifferentFocusAndAllReasonsWithoutWeeklyBacklog()
	{
		var rows = new[] { Row(1, "2026-10-02"), Row(2, due: "2026-10-02T13:00Z"),
			Row(3, "2026-10-10", "2026-10-02T10:00Z") with { IsImportant = true },
			Row(4, "2026-10-02", "2026-10-02T10:00Z"), Row(5, due: "2025-01-01"),
			Row(6, "2026-10-01"), Row(7, "2026-10-03"), Row(8, due: "2026-10-03"), Row(9, "2026-10-02", completed: true) };
		var r = DayReportBuilder.Build(Today, rows);
		r.Sections.Select(s => s.Name).Should().Equal("today", "overdue");
		r.Tasks.Should().HaveCount(5).And.OnlyHaveUniqueItems(t => t.Id);
		r.Sections[0].TotalCount.Should().Be(4);
		r.Sections[1].MatchCount.Should().Be(3);
		r.Sections[1].TotalCount.Should().Be(1);
		r.Tasks.Single(t => t.Id == rows[3].Id).Reasons.Should().Equal("plannedToday", "dueToday", "overdueDeadline");
		var different = r.Tasks.Single(t => t.Id == rows[2].Id);
		different.Reasons.Should().Equal("dueToday", "overdueDeadline");
		different.IsImportant.Should().BeTrue();
		different.FocusDate.Should().Be(new DateOnly(2026, 10, 10));
		different.DueDate.Should().Be(new DateOnly(2026, 10, 2));
	}

	[Fact]
	public void ExactCountsBoundedPagesIncludeOverlapsAndTitlesAreSurrogateSafe()
	{
		var rows = Enumerable.Range(1, 13).Select(i => Row(i, "2026-10-02", "2026-10-02T10:00Z")
			with { Title = new string('a', 79) + "😀extra" }).ToArray();
		var r = DayReportBuilder.Build(Today, rows.Reverse().ToArray());
		r.Tasks.Should().HaveCount(5);
		r.Sections[0].TotalCount.Should().Be(13);
		r.Sections[0].Truncated.Should().BeTrue();
		r.Sections[1].MatchCount.Should().Be(13);
		r.Sections[1].TotalCount.Should().Be(0);
		var first = DayReportBuilder.Build(Today, rows, new("overdue", Limit: 10));
		var next = DayReportBuilder.Build(Today, rows, new("overdue", 10, 10));
		first.Sections.Single().HasMore.Should().BeTrue();
		next.Sections.Single().HasMore.Should().BeFalse();
		next.Sections.Single().Truncated.Should().BeTrue();
		first.Tasks.Concat(next.Tasks).Select(t => t.Id).Should().Equal(rows.Select(t => t.Id));
		first.Tasks.Should().OnlyContain(t => t.Title.Length == 79 && t.TitleTruncated);
		var end = DayReportBuilder.Build(Today, rows, new("today", 100, 20));
		end.Tasks.Should().BeEmpty(); end.Sections.Single().TotalCount.Should().Be(13);
		end.Sections.Single().HasMore.Should().BeFalse();
	}

	[Fact]
	public void EmptyTodayIsExplicitWithOnlyBoundedOverdueWarnings()
	{
		var r = DayReportBuilder.Build(Today, Enumerable.Range(1, 100).Select(i => Row(i, due: "2025-01-01")).ToArray());
		r.Sections[0].TotalCount.Should().Be(0); r.Sections[0].Truncated.Should().BeFalse();
		r.Sections[1].TotalCount.Should().Be(100); r.Tasks.Should().HaveCount(5);
		var empty = DayReportBuilder.Build(Today, []);
		empty.Sections.Should().OnlyContain(s => s.TotalCount == 0 && !s.Truncated && !s.HasMore);
	}

	[Theory]
	[InlineData("Asia/Tokyo", "2026-10-04T16:00Z", "2026-10-04T15:00Z", "2026-10-05T15:00Z", "2026-10-05")]
	[InlineData("America/Los_Angeles", "2026-10-05T02:00Z", "2026-10-04T07:00Z", "2026-10-05T07:00Z", "2026-10-04")]
	[InlineData("America/New_York", "2026-03-08T12:00Z", "2026-03-08T05:00Z", "2026-03-09T04:00Z", "2026-03-08")]
	[InlineData("America/New_York", "2026-11-01T12:00Z", "2026-11-01T04:00Z", "2026-11-02T05:00Z", "2026-11-01")]
	public void LocalHalfOpenDayPreservesFocusDatesAndDeadlineInstants(string zone, string now, string start, string end, string date)
	{
		var today = PlanningCalendarResolver.Resolve("today", Utc(now), zone);
		var rows = new[] { Row(1, date), Row(2, due: start) with { DueAt = DateTime.SpecifyKind(Utc(start), DateTimeKind.Unspecified) },
			Row(3, due: end), Row(4, due: start) with { DueAt = Utc(start).AddTicks(-1) } };
		var r = DayReportBuilder.Build(today, rows, new("today"));
		r.Tasks.Select(t => t.Id).Should().BeEquivalentTo(rows.Take(2).Select(t => t.Id));
		r.Tasks.Single(t => t.Id == rows[1].Id).DueDate.Should().Be(DateOnly.Parse(date));
		r.Tasks.Single(t => t.Id == rows[1].Id).DueAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
		var overdue = DayReportBuilder.Build(today, rows, new("overdue"));
		overdue.Tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { rows[1].Id, rows[3].Id });
	}

	[Fact]
	public void DeadlineAtAsOfIsNotOverdueAndSamplingCarriesImportanceWithoutRankingCapacity()
	{
		var r = DayReportBuilder.Build(Today, [Row(1, due: "2026-10-02T12:00Z"), Row(2, due: "2026-10-02T12:00Z") with { IsImportant = true }]);
		r.Sections[1].MatchCount.Should().Be(0);
		r.Tasks[0].IsImportant.Should().BeTrue();
	}

	[Theory]
	[InlineData("olderFocus", 0, 5)]
	[InlineData("today", -1, 5)]
	[InlineData("today", 0, 0)]
	[InlineData("today", 0, 21)]
	[InlineData("all", 1, 5)]
	public void InvalidOptionsAreRejected(string section, int offset, int limit)
	{
		var action = () => new DayReportOptions(section, offset, limit).Validate();
		action.Should().Throw<ArgumentException>();
	}
}
