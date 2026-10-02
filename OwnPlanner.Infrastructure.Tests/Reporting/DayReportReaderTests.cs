using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public sealed partial class CalendarReportReaderTests
{
	[Fact]
	public async Task DayReadIsEligibleFreshBoundedAndPreservesImportanceWithoutWrites()
	{
		await using var f = new Fixture("2026-10-02T12:00Z");
		var ct = TestContext.Current.CancellationToken;
		await f.Preferences("Asia/Tokyo", 0);
		var list = new TaskList("Active"); var archive = new TaskList("Archive"); archive.Archive();
		f.Db.AddRange(list, archive); await f.Db.SaveChangesAsync(ct);
		var rows = Enumerable.Range(1, 8).Select(i => new TaskItem($"Due {i}", list.Id, dueAt: Utc("2026-10-02T10:00Z"), isImportant: i == 1)).ToArray();
		rows[0].SetFocusAt(Utc("2026-10-10"));
		var trash = new TaskItem("Trash", list.Id, dueAt: Utc("2026-10-02T10:00Z")); trash.Trash();
		var hidden = new TaskItem("Archive", archive.Id, dueAt: Utc("2026-10-02T10:00Z"));
		var done = new TaskItem("Done", list.Id, dueAt: Utc("2026-10-02T10:00Z")); done.Complete();
		f.Db.AddRange(rows); f.Db.AddRange(trash, hidden, done); await f.Db.SaveChangesAsync(ct);
		var r = await f.General.GetDayAsync(new(), ct);
		r.Sections[0].TotalCount.Should().Be(8); r.Tasks.Should().HaveCount(5);
		r.Tasks[0].Id.Should().Be(rows[0].Id); r.Tasks[0].IsImportant.Should().BeTrue();
		r.Tasks[0].FocusDate.Should().Be(new DateOnly(2026, 10, 10));
		r.Sections[1].MatchCount.Should().Be(8); r.Sections[1].TotalCount.Should().Be(0);
		var page = await f.General.GetDayAsync(new("overdue", 5), ct);
		page.Tasks.Should().HaveCount(3); page.Sections.Single().TotalCount.Should().Be(8);
		rows[0].Complete(); await f.Db.SaveChangesAsync(ct);
		var fresh = await f.General.GetDayAsync(new(), ct);
		fresh.Sections[0].TotalCount.Should().Be(7); fresh.Tasks.Should().NotContain(t => t.Id == rows[0].Id);
		f.Clock.Now = Utc("2026-10-02T15:01Z");
		var midnight = await f.General.GetDayAsync(new(), ct);
		midnight.Period.Today.Should().Be(new DateOnly(2026, 10, 3)); midnight.Sections[0].TotalCount.Should().Be(0);
		midnight.Sections[1].TotalCount.Should().Be(7);
		(await f.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
		(await f.Db.WeeklyReviewPreferences.SingleAsync(ct)).Enabled.Should().BeFalse();
	}

	[Theory]
	[InlineData("Asia/Tokyo", "2026-10-04T16:00Z", "2026-10-05", "2026-10-04T15:00Z", "2026-10-05T15:00Z")]
	[InlineData("America/Los_Angeles", "2026-10-05T02:00Z", "2026-10-04", "2026-10-04T07:00Z", "2026-10-05T07:00Z")]
	[InlineData("America/New_York", "2026-03-08T12:00Z", "2026-03-08", "2026-03-08T05:00Z", "2026-03-09T04:00Z")]
	[InlineData("America/New_York", "2026-11-01T12:00Z", "2026-11-01", "2026-11-01T04:00Z", "2026-11-02T05:00Z")]
	public async Task DayReaderUsesSharedCalendarAndStoredFocusDate(string zone, string now, string date, string start, string end)
	{
		await using var f = new Fixture(now);
		var ct = TestContext.Current.CancellationToken;
		await f.Preferences(zone);
		var list = new TaskList("Tasks"); f.Db.Add(list); await f.Db.SaveChangesAsync(ct);
		var focus = new TaskItem("Focus", list.Id); focus.SetFocusAt(Utc(date));
		var first = new TaskItem("Start", list.Id, dueAt: Utc(start));
		var last = new TaskItem("End", list.Id, dueAt: Utc(end));
		f.Db.AddRange(focus, first, last); await f.Db.SaveChangesAsync(ct);
		var r = await f.General.GetDayAsync(new("today"), ct);
		r.Tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { focus.Id, first.Id });
		r.Period.StartsAtUtc.Should().Be(Utc(start)); r.Period.EndsAtUtc.Should().Be(Utc(end));
	}

	[Fact]
	public async Task EmptyFallbackDayDoesNotCreatePreferencesOrReviews()
	{
		await using var f = new Fixture("2026-10-02T12:00Z");
		var ct = TestContext.Current.CancellationToken;
		var r = await f.General.GetDayAsync(new(), ct);
		r.Tasks.Should().BeEmpty(); r.Period.FallbackExplanation.Should().Contain("UTC");
		(await f.Db.WeeklyReviewPreferences.CountAsync(ct)).Should().Be(0);
		(await f.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
	}
}
