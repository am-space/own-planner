using FluentAssertions;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;

namespace OwnPlanner.Application.Tests.Reporting;

public sealed class GeneralAttentionBuilderTests
{
	private static DateTime Utc(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
	private static GeneralTaskRow Row(int id, string? focus = null, string? due = null, bool completed = false, string title = "Task") =>
		new(new Guid(id, 0, 0, new byte[8]), title, completed, focus is null ? null : Utc(focus), due is null ? null : Utc(due), WellKnownIds.InboxTaskList, null);
	private static GeneralAttentionSection Section(GeneralAttentionReport r, string name) => r.Sections.Single(s => s.Name == name);

	[Fact]
	public void MidweekOverviewShowsEachTaskOnceAndPreservesEveryReasonAndExactCounts()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-01T12:00Z"), "UTC");
		var tasks = new[] {
			Row(1, "2026-10-01", "2026-10-01T10:00Z"), // today + deadline expired earlier today
			Row(2, "2026-09-29", "2026-10-02T10:00Z"), // earlier focus + future deadline
			Row(3, "2026-10-03"), Row(4, due: "2026-10-02T10:00Z"),
			Row(5, due: "2026-08-01T10:00Z"), Row(6, "2026-09-01"),
			Row(7, "2026-10-01", completed: true), Row(8, "2026-10-05"),
			Row(9, "2026-10-02", "2026-08-01T10:00Z"), Row(10, "2026-09-01", "2026-08-01T10:00Z")
		};
		var r = GeneralAttentionBuilder.Build(week, tasks);
		r.Sections.Select(s => s.Name).Should().Equal("today", "earlierThisWeek", "remainingWeek", "overdue", "olderFocus");
		Section(r, "today").TotalCount.Should().Be(1);
		Section(r, "earlierThisWeek").TotalCount.Should().Be(1);
		Section(r, "remainingWeek").TotalCount.Should().Be(3);
		Section(r, "remainingWeek").MatchCount.Should().Be(4);
		Section(r, "remainingWeek").Days.Should().BeEquivalentTo(new[] { new GeneralAttentionDay(new(2026, 10, 2), 2), new GeneralAttentionDay(new(2026, 10, 3), 1) });
		Section(r, "overdue").MatchCount.Should().Be(4);
		Section(r, "overdue").TotalCount.Should().Be(2);
		Section(r, "olderFocus").MatchCount.Should().Be(2);
		Section(r, "olderFocus").TotalCount.Should().Be(1);
		Section(r, "olderFocus").TaskIds.Should().BeEmpty();
		Section(r, "olderFocus").Truncated.Should().BeTrue();
		r.Tasks.Should().OnlyHaveUniqueItems(t => t.Id).And.HaveCount(7);
		r.Tasks.Single(t => t.Id == tasks[0].Id).Reasons.Should().Equal("plannedToday", "dueToday", "overdueDeadline");
		r.Tasks.Single(t => t.Id == tasks[1].Id).Reasons.Should().Equal("missedFocusThisWeek", "dueLaterThisWeek");
		r.Tasks.Single(t => t.Id == tasks[8].Id).Reasons.Should().Equal("plannedLaterThisWeek", "overdueDeadline");
		var overdue = GeneralAttentionBuilder.Build(week, tasks, new("overdue"));
		overdue.Sections.Single().TotalCount.Should().Be(4);
		overdue.Tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { tasks[0].Id, tasks[4].Id, tasks[8].Id, tasks[9].Id });
	}

	[Fact]
	public void SectionPagesAreStableBoundedAndRetrieveAllMatchesIncludingOverlaps()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-01T12:00Z"), "UTC");
		var tasks = Enumerable.Range(1, 13).Select(i => Row(i, "2026-10-01", "2026-10-01T10:00Z", title: new string('a', 79) + "😀extra")).ToArray();
		var overview = GeneralAttentionBuilder.Build(week, tasks.Reverse().ToArray());
		Section(overview, "today").TotalCount.Should().Be(13);
		Section(overview, "today").TaskIds.Should().Equal(tasks.Take(5).Select(t => t.Id));
		Section(overview, "overdue").TotalCount.Should().Be(0);
		var first = GeneralAttentionBuilder.Build(week, tasks, new("overdue", Limit: 10));
		var last = GeneralAttentionBuilder.Build(week, tasks, new("overdue", 10, 10));
		first.Sections.Single().HasMore.Should().BeTrue();
		last.Sections.Single().HasMore.Should().BeFalse();
		last.Sections.Single().Truncated.Should().BeTrue();
		first.Tasks.Concat(last.Tasks).Select(t => t.Id).Should().Equal(tasks.Select(t => t.Id));
		first.Tasks.Should().OnlyContain(t => t.Title.Length == 79 && t.TitleTruncated);
		var pastEnd = GeneralAttentionBuilder.Build(week, tasks, new("overdue", 100, 10));
		pastEnd.Tasks.Should().BeEmpty();
		pastEnd.Sections.Single().TotalCount.Should().Be(13);
		pastEnd.Sections.Single().HasMore.Should().BeFalse();
	}

	[Theory]
	[InlineData("Asia/Tokyo", "2026-10-04T16:00Z", "2026-10-04T15:00Z", "2026-10-05T15:00Z", "2026-10-05")]
	[InlineData("America/Los_Angeles", "2026-10-05T02:00Z", "2026-10-04T07:00Z", "2026-10-05T07:00Z", "2026-10-04")]
	[InlineData("America/New_York", "2026-03-08T12:00Z", "2026-03-08T05:00Z", "2026-03-09T04:00Z", "2026-03-08")]
	[InlineData("America/New_York", "2026-11-01T12:00Z", "2026-11-01T04:00Z", "2026-11-02T05:00Z", "2026-11-01")]
	public void LocalTodayBoundariesAndStoredFocusDatesRemainDistinct(string zone, string now, string start, string end, string date)
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc(now), zone);
		var rows = new[] { Row(1, date), Row(2, due: start), Row(3, due: end), Row(4, due: start) with { DueAt = Utc(start).AddTicks(-1) } };
		var r = GeneralAttentionBuilder.Build(week, rows, new("today"));
		r.Tasks.Select(t => t.Id).Should().BeEquivalentTo(rows.Take(2).Select(t => t.Id));
		r.Tasks.Single(t => t.Id == rows[0].Id).FocusDate.Should().Be(DateOnly.Parse(date));
		r.Tasks.Single(t => t.Id == rows[1].Id).DueDate.Should().Be(DateOnly.Parse(date));
		r.TodayPeriod.StartsAtUtc.Should().Be(Utc(start));
		r.TodayPeriod.EndsAtUtc.Should().Be(Utc(end));
	}

	[Fact]
	public void FinalDayKeepsTodayAndEarlierPlansWithoutAdvancingToReviewTarget()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-03T12:00Z"), "UTC", 0);
		var r = GeneralAttentionBuilder.Build(week, [Row(1, "2026-10-03"), Row(2, "2026-09-27"), Row(3, "2026-10-04")]);
		r.Period.StartDate.Should().Be(new DateOnly(2026, 9, 27));
		Section(r, "today").TotalCount.Should().Be(1);
		Section(r, "earlierThisWeek").TotalCount.Should().Be(1);
		Section(r, "remainingWeek").TotalCount.Should().Be(0);
	}

	[Fact]
	public void EmptyAndOlderFocusOnlyReportsStayCompactAndPermitExplicitDrillDown()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-01T12:00Z"), null);
		var empty = GeneralAttentionBuilder.Build(week, []);
		empty.Tasks.Should().BeEmpty();
		empty.Sections.Should().OnlyContain(s => s.TotalCount == 0 && !s.Truncated && !s.HasMore);
		empty.Period.FallbackExplanation.Should().Contain("UTC");
		var rows = Enumerable.Range(1, 100).Select(i => Row(i) with { FocusAt = Utc("2025-01-01").AddDays(i) }).ToArray();
		var r = GeneralAttentionBuilder.Build(week, rows);
		r.Tasks.Should().BeEmpty();
		Section(r, "olderFocus").Days.Should().BeEmpty();
		Section(r, "olderFocus").TotalCount.Should().Be(100);
		GeneralAttentionBuilder.Build(week, rows, new("olderFocus")).Tasks.Should().HaveCount(5);
	}

	[Fact]
	public void FutureDaysHaveExactCountsEvenBeyondSamplesAndKeepBothRelevantDates()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-09-30T12:00Z"), "UTC");
		var rows = Enumerable.Range(1, 8).Select(i => Row(i, "2026-10-02", "2026-10-01T10:00Z")).ToList();
		rows.Add(Row(9, "2026-10-04", "2026-10-05T00:00Z")); // week-end exclusive deadline is outside this week
		var r = GeneralAttentionBuilder.Build(week, rows);
		var section = Section(r, "remainingWeek");
		section.TotalCount.Should().Be(9);
		section.TaskIds.Should().HaveCount(5);
		section.Truncated.Should().BeTrue();
		section.Days.Should().BeEquivalentTo(new[] { new GeneralAttentionDay(new(2026, 10, 1), 8), new GeneralAttentionDay(new(2026, 10, 4), 1) });
		r.Tasks.Should().OnlyContain(t => t.DisplayDate == new DateOnly(2026, 10, 1)
			&& t.FocusDate == new DateOnly(2026, 10, 2) && t.DueDate == new DateOnly(2026, 10, 1)
			&& t.Reasons.SequenceEqual(new[] { "plannedLaterThisWeek", "dueLaterThisWeek" }));
		var page = GeneralAttentionBuilder.Build(week, rows, new("remainingWeek", 8));
		page.Tasks.Should().ContainSingle(t => t.Id == rows[8].Id && t.Reasons.SequenceEqual(new[] { "plannedLaterThisWeek" }));
	}

	[Fact]
	public void MaterializedDeadlineKeepsUtcInstantAndExplicitWireMarker()
	{
		var week = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-01T12:00Z"), "Asia/Tokyo");
		var row = Row(1) with { DueAt = DateTime.SpecifyKind(Utc("2026-10-01T10:00Z"), DateTimeKind.Unspecified) };
		var r = GeneralAttentionBuilder.Build(week, [row]);
		r.Tasks.Single().DueAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
		var json = System.Text.Json.JsonSerializer.SerializeToElement(r, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
		json.GetProperty("tasks")[0].GetProperty("dueAt").GetString().Should().Be("2026-10-01T10:00:00Z");
		r.Tasks.Single().DueDate.Should().Be(new DateOnly(2026, 10, 1));
	}

	[Theory]
	[InlineData("invalid", 0, 5)]
	[InlineData("today", -1, 5)]
	[InlineData("today", 0, 0)]
	[InlineData("today", 0, 21)]
	[InlineData("all", 1, 5)]
	public void InvalidPageOptionsAreRejected(string section, int offset, int limit)
	{
		var action = () => new GeneralAttentionOptions(section, offset, limit).Validate();
		action.Should().Throw<ArgumentException>();
	}
}
