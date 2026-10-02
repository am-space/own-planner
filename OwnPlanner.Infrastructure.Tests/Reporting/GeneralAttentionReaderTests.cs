using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public sealed partial class CalendarReportReaderTests
{
	[Fact]
	public async Task AttentionUsesEligibleCurrentStatePreservesLegacyFieldsAndPagesOlderWork()
	{
		await using var f = new Fixture("2026-10-01T12:00:00Z");
		var ct = TestContext.Current.CancellationToken;
		await f.Preferences("Asia/Tokyo", 0);
		var list = new TaskList("Active"); var archive = new TaskList("Archive"); archive.Archive();
		f.Db.AddRange(list, archive); await f.Db.SaveChangesAsync(ct);
		var today = new TaskItem("Today", list.Id, dueAt: Utc("2026-10-01T11:00Z")); today.SetFocusAt(Utc("2026-10-01"));
		var earlier = new TaskItem("Earlier", list.Id); earlier.SetFocusAt(Utc("2026-09-28"));
		var future = new TaskItem("Future", list.Id); future.SetFocusAt(Utc("2026-10-03"));
		var old = new TaskItem("Older focus", list.Id); old.SetFocusAt(Utc("2026-09-20"));
		var expired = new TaskItem("Old deadline", list.Id, dueAt: Utc("2026-08-01"));
		var trash = new TaskItem("Trash", list.Id, dueAt: Utc("2026-08-01")); trash.Trash();
		var hidden = new TaskItem("Archived", archive.Id, dueAt: Utc("2026-08-01"));
		var done = new TaskItem("Done", list.Id, dueAt: Utc("2026-08-01")); done.Complete();
		f.Db.AddRange(today, earlier, future, old, expired, trash, hidden, done); await f.Db.SaveChangesAsync(ct);
		var before = await f.General.GetAsync(ct);
		var calendar = await f.General.GetCalendarAsync("thisWeek", ct);
		(calendar with { Calendar = null }).Should().BeEquivalentTo(before);
		var attention = await f.General.GetAttentionAsync(new(), ct);
		attention.Should().BeEquivalentTo(calendar.Calendar!.Attention);
		attention.Tasks.Select(t => t.Title).Should().BeEquivalentTo("Today", "Earlier", "Future", "Old deadline");
		attention.Tasks.Where(t => t.DueAt.HasValue).Should().OnlyContain(t => t.DueAt!.Value.Kind == DateTimeKind.Utc);
		attention.Sections.Single(s => s.Name == "olderFocus").TotalCount.Should().Be(1);
		attention.Period.StartDate.Should().Be(new DateOnly(2026, 9, 27));
		var oldPage = await f.General.GetAttentionAsync(new("olderFocus"), ct);
		oldPage.Tasks.Should().ContainSingle(t => t.Id == old.Id);
		var overdue = await f.General.GetAttentionAsync(new("overdue", Limit: 1), ct);
		overdue.Sections.Single().TotalCount.Should().Be(2);
		overdue.Sections.Single().HasMore.Should().BeTrue();
		var next = await f.General.GetAttentionAsync(new("overdue", 1, 1), ct);
		next.Tasks.Should().ContainSingle(t => t.Id == today.Id);
		today.Complete(); await f.Db.SaveChangesAsync(ct);
		var fresh = await f.General.GetAttentionAsync(new(), ct);
		fresh.Sections.Single(s => s.Name == "today").TotalCount.Should().Be(0);
		fresh.Sections.Single(s => s.Name == "overdue").MatchCount.Should().Be(1);
		(await f.General.GetCalendarAsync("today", ct)).Calendar!.Attention.Should().BeNull();
		(await f.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
		(await f.Db.WeeklyReviewPreferences.SingleAsync(ct)).Enabled.Should().BeFalse();
	}

	[Theory]
	[InlineData("Asia/Tokyo", "2026-10-04T16:00Z", "2026-10-05", "2026-10-04T15:00Z", "2026-10-05T15:00Z")]
	[InlineData("America/Los_Angeles", "2026-10-05T02:00Z", "2026-10-04", "2026-10-04T07:00Z", "2026-10-05T07:00Z")]
	[InlineData("America/New_York", "2026-03-08T12:00Z", "2026-03-08", "2026-03-08T05:00Z", "2026-03-09T04:00Z")]
	[InlineData("America/New_York", "2026-11-01T12:00Z", "2026-11-01", "2026-11-01T04:00Z", "2026-11-02T05:00Z")]
	public async Task AttentionReaderUsesSharedLocalHalfOpenDayBoundaries(string zone, string now, string date, string start, string end)
	{
		await using var f = new Fixture(now);
		var ct = TestContext.Current.CancellationToken;
		await f.Preferences(zone);
		var list = new TaskList("Tasks"); f.Db.Add(list); await f.Db.SaveChangesAsync(ct);
		var focus = new TaskItem("Stored date", list.Id); focus.SetFocusAt(Utc(date));
		var first = new TaskItem("Day start", list.Id, dueAt: Utc(start));
		var last = new TaskItem("Day end", list.Id, dueAt: Utc(end));
		f.Db.AddRange(focus, first, last); await f.Db.SaveChangesAsync(ct);
		var r = await f.General.GetAttentionAsync(new("today"), ct);
		r.Tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { focus.Id, first.Id });
		r.TodayPeriod.Today.Should().Be(DateOnly.Parse(date));
		r.TodayPeriod.StartsAtUtc.Should().Be(Utc(start));
		r.TodayPeriod.EndsAtUtc.Should().Be(Utc(end));
	}

	[Fact]
	public async Task AttentionRefreshesAtMidnightAndFallbackDoesNotCreatePreferencesOrReviews()
	{
		await using var f = new Fixture("2026-10-04T23:59Z");
		var ct = TestContext.Current.CancellationToken;
		var list = new TaskList("Tasks"); f.Db.Add(list); await f.Db.SaveChangesAsync(ct);
		var sunday = new TaskItem("Sunday", list.Id); sunday.SetFocusAt(Utc("2026-10-04"));
		var monday = new TaskItem("Monday", list.Id); monday.SetFocusAt(Utc("2026-10-05"));
		f.Db.AddRange(sunday, monday); await f.Db.SaveChangesAsync(ct);
		var before = await f.General.GetAttentionAsync(new(), ct);
		before.Tasks.Should().ContainSingle(t => t.Id == sunday.Id);
		before.Period.StartDate.Should().Be(new DateOnly(2026, 9, 28));
		before.Period.FallbackExplanation.Should().Contain("UTC");
		f.Clock.Now = f.Clock.Now.AddMinutes(2);
		var after = await f.General.GetAttentionAsync(new(), ct);
		after.Tasks.Should().ContainSingle(t => t.Id == monday.Id);
		after.Period.StartDate.Should().Be(new DateOnly(2026, 10, 5));
		after.Sections.Single(s => s.Name == "olderFocus").TotalCount.Should().Be(1);
		(await f.Db.WeeklyReviewPreferences.CountAsync(ct)).Should().Be(0);
		(await f.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
	}
}
