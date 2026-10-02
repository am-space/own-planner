using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public sealed partial class CalendarReportReaderTests
{
	[Theory]
	[InlineData("Asia/Tokyo", "2026-10-04T16:00:00Z", "2026-10-05", "2026-10-04T15:00:00Z", "2026-10-11T15:00:00Z")]
	[InlineData("America/Los_Angeles", "2026-10-05T02:00:00Z", "2026-09-28", "2026-09-28T07:00:00Z", "2026-10-05T07:00:00Z")]
	[InlineData("America/New_York", "2026-03-08T12:00:00Z", "2026-03-02", "2026-03-02T05:00:00Z", "2026-03-09T04:00:00Z")]
	[InlineData("America/New_York", "2026-11-01T12:00:00Z", "2026-10-26", "2026-10-26T04:00:00Z", "2026-11-02T05:00:00Z")]
	public async Task WeeklyReportsKeepFocusDatesAndUseLocalHalfOpenDeadlineBoundaries(string zone, string now, string firstDate, string start, string end)
	{
		await using var fixture = new Fixture(now);
		var ct = TestContext.Current.CancellationToken;
		await fixture.Preferences(zone);
		var list = new TaskList("Work");
		fixture.Db.Add(list);
		await fixture.Db.SaveChangesAsync(ct);
		var first = DateOnly.Parse(firstDate).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var goal = new OwnPlanner.Domain.Goals.Goal("Goal", OwnPlanner.Domain.Goals.GoalHorizon.Yearly, targetPeriod: "2026");
		fixture.Db.Add(goal);
		await fixture.Db.SaveChangesAsync(ct);
		var focused = new TaskItem("Stored calendar date", list.Id, "Focus preview", goalId: goal.Id);
		focused.SetFocusAt(first);
		var dueAtStart = new TaskItem("At inclusive start", list.Id, "Due preview", dueAt: Utc(start), goalId: goal.Id);
		var outside = new TaskItem("At exclusive end", list.Id, dueAt: Utc(end));
		outside.SetFocusAt(first.AddDays(7));
		var before = new TaskItem("Before start", list.Id, dueAt: Utc(start).AddTicks(-1));
		fixture.Db.AddRange(focused, dueAtStart, outside, before);
		await fixture.Db.SaveChangesAsync(ct);
		var report = await fixture.Weekly.GetAsync(new(CalendarPeriod: "thisWeek"), ct);
		report.Calendar!.StartsAtUtc.Should().Be(Utc(start));
		report.Calendar.EndsAtUtc.Should().Be(Utc(end));
		report.TimeZone.Should().Be(zone);
		report.Totals.FocusedInsideWindowCount.Should().Be(1);
		report.Totals.DueInsideWindowCount.Should().Be(1);
		report.Totals.DistinctWindowTaskCount.Should().Be(2);
		report.Days[0].FocusedTaskSamples.Should().ContainSingle(t => t.Id == focused.Id && t.DescriptionPreview == "Focus preview");
		report.Days[0].DueTaskSamples.Should().ContainSingle(t => t.Id == dueAtStart.Id && t.DescriptionPreview == "Due preview");
		report.Days.Sum(d => d.DueTaskCount).Should().Be(1);
		report.Goals.Single().WindowTaskCount.Should().Be(2);
		report.Contexts.Single().WindowTaskCount.Should().Be(2);
		(await fixture.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
	}

	[Fact]
	public async Task GeneralAddsLocalTodayAndWeekWithoutReinterpretingLegacyUtcRollingFields()
	{
		await using var fixture = new Fixture("2026-10-04T16:00:00Z"); // Monday morning in Tokyo
		var ct = TestContext.Current.CancellationToken;
		await fixture.Preferences("Asia/Tokyo");
		var list = new TaskList("Tasks"); fixture.Db.Add(list);
		await fixture.Db.SaveChangesAsync(ct);
		var localToday = new TaskItem("Monday focus", list.Id, dueAt: Utc("2026-10-04T15:00:00Z"));
		localToday.SetFocusAt(Utc("2026-10-05T00:00:00Z"));
		var localEnd = new TaskItem("Next Monday deadline", list.Id, dueAt: Utc("2026-10-11T15:00:00Z"));
		fixture.Db.AddRange(localToday, localEnd);
		await fixture.Db.SaveChangesAsync(ct);
		var legacy = await fixture.General.GetAsync(ct);
		var report = await fixture.General.GetCalendarAsync("thisWeek", ct);
		(report with { Calendar = null }).Should().BeEquivalentTo(legacy);
		report.TodayDate.Should().Be(new DateOnly(2026, 10, 4));
		report.UpcomingEndExclusiveDate.Should().Be(new DateOnly(2026, 10, 12));
		report.Calendar!.TodayPeriod.Today.Should().Be(new DateOnly(2026, 10, 5));
		report.Calendar.Today.FocusTaskCount.Should().Be(1);
		report.Calendar.DueTodayCount.Should().Be(1);
		report.Calendar.FocusedInsidePeriodCount.Should().Be(1);
		report.Calendar.DueInsidePeriodCount.Should().Be(1);
		report.Calendar.PeriodTasks.TotalCount.Should().Be(1); // same task matched focus and due
		localToday.Complete(); await fixture.Db.SaveChangesAsync(ct);
		var fresh = await fixture.General.GetCalendarAsync("thisWeek", ct);
		fresh.Calendar!.Today.CompletedCount.Should().Be(1);
		fresh.Calendar.PeriodTasks.TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task ReflectionUsesPreviousCompleteLocalWeekAndRetainsExplicitRollingContract()
	{
		await using var fixture = new Fixture("2026-10-04T16:00:00Z"); // Monday in Tokyo
		var ct = TestContext.Current.CancellationToken;
		await fixture.Preferences("Asia/Tokyo");
		var list = new TaskList("Tasks"); fixture.Db.Add(list);
		await fixture.Db.SaveChangesAsync(ct);
		var first = new TaskItem("Completed start", list.Id); first.Complete();
		var end = new TaskItem("Completed end", list.Id); end.Complete();
		var focus = new TaskItem("Previous Monday focus", list.Id); focus.SetFocusAt(Utc("2026-09-28T00:00:00Z"));
		var focusEnd = new TaskItem("This Monday focus", list.Id); focusEnd.SetFocusAt(Utc("2026-10-05T00:00:00Z"));
		fixture.Db.AddRange(first, end, focus, focusEnd);
		await fixture.Db.SaveChangesAsync(ct);
		fixture.Db.Entry(first).Property(t => t.CompletedAt).CurrentValue = Utc("2026-09-27T15:00:00Z");
		fixture.Db.Entry(end).Property(t => t.CompletedAt).CurrentValue = Utc("2026-10-04T15:00:00Z");
		await fixture.Db.SaveChangesAsync(ct);
		var report = await fixture.Reflection.GetAsync(new(CalendarPeriod: "lastWeek"), ct);
		report.PeriodStartUtc.Should().Be(Utc("2026-09-27T15:00:00Z"));
		report.PeriodEndExclusiveUtc.Should().Be(Utc("2026-10-04T15:00:00Z"));
		report.Totals.CompletedTaskCount.Should().Be(1);
		report.Totals.MissedFocusTaskCount.Should().Be(1);
		report.Signals.FocusedButIncompleteTasks.Should().ContainSingle(t => t.Id == focus.Id);
		report.HistoricalLimitations.Should().Contain(t => t.Contains("reopened"));
		var rolling = await fixture.Reflection.GetAsync(new(CalendarPeriod: "lastSevenDays"), ct);
		rolling.PeriodEndExclusiveUtc.Should().Be(fixture.Clock.Now);
		rolling.Totals.CompletedTaskCount.Should().Be(1); // end task is now inside; first task precedes rolling start
		var legacy = await fixture.Reflection.GetAsync(new(), ct);
		legacy.Calendar.Should().BeNull();
		legacy.TimeZone.Should().Be("UTC");
		legacy.PeriodStartUtc.Should().Be(fixture.Clock.Now.AddDays(-7));
	}

	[Fact]
	public async Task NamedWindowsAndRepeatedReadsResolveAgainstFreshClockWithoutWritingPreferences()
	{
		await using var fixture = new Fixture("2026-10-04T23:59:00Z");
		var ct = TestContext.Current.CancellationToken;
		var week = await fixture.Weekly.GetAsync(new(CalendarPeriod: "thisWeek"), ct);
		var next = await fixture.Weekly.GetAsync(new(CalendarPeriod: "nextWeek"), ct);
		var seven = await fixture.Weekly.GetAsync(new(CalendarPeriod: "nextSevenDays"), ct);
		week.WindowStartDate.Should().Be(new DateOnly(2026, 9, 28));
		next.WindowStartDate.Should().Be(new DateOnly(2026, 10, 5));
		seven.WindowStartDate.Should().Be(new DateOnly(2026, 10, 4));
		week.Calendar!.FallbackExplanation.Should().Contain("UTC");
		fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
		var after = await fixture.Weekly.GetAsync(new(CalendarPeriod: "thisWeek"), ct);
		after.WindowStartDate.Should().Be(new DateOnly(2026, 10, 5));
		(await fixture.Db.WeeklyReviewPreferences.CountAsync(ct)).Should().Be(0);
		(await fixture.Db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
	}

	[Fact]
	public async Task LocalSamplesAreBoundedAndExcludeTrashCompletedAndArchivedWork()
	{
		await using var fixture = new Fixture("2026-10-02T12:00:00Z");
		var ct = TestContext.Current.CancellationToken;
		await fixture.Preferences("UTC", 0);
		var list = new TaskList("Active"); var archive = new TaskList("Archived"); archive.Archive();
		fixture.Db.AddRange(list, archive); await fixture.Db.SaveChangesAsync(ct);
		for (var i = 0; i < 8; i++)
		{
			var t = new TaskItem($"Focus {i}", list.Id); t.SetFocusAt(Utc("2026-10-02T00:00:00Z")); fixture.Db.Add(t);
		}
		var hidden = new TaskItem("Archived", archive.Id); hidden.SetFocusAt(Utc("2026-10-02T00:00:00Z"));
		var trash = new TaskItem("Trash", list.Id); trash.SetFocusAt(Utc("2026-10-02T00:00:00Z")); trash.Trash();
		var complete = new TaskItem("Done", list.Id); complete.SetFocusAt(Utc("2026-10-02T00:00:00Z")); complete.Complete();
		fixture.Db.AddRange(hidden, trash, complete); await fixture.Db.SaveChangesAsync(ct);
		var weekly = await fixture.Weekly.GetAsync(new(TaskSampleLimit: 1, CalendarPeriod: "thisWeek"), ct);
		weekly.WindowStartDate.Should().Be(new DateOnly(2026, 9, 27));
		weekly.Totals.FocusedInsideWindowCount.Should().Be(8);
		weekly.Days.Single(d => d.Date == new DateOnly(2026, 10, 2)).FocusedTaskSamples.Should().ContainSingle();
		var general = await fixture.General.GetCalendarAsync("thisWeek", ct);
		general.Calendar!.PeriodTasks.TotalCount.Should().Be(8);
		general.Calendar.PeriodTasks.TaskIds.Should().HaveCount(5);
		general.Calendar.PeriodTasks.Truncated.Should().BeTrue();
	}

	private static DateTime Utc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
	private sealed class MutableClock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
	private sealed class Fixture : IAsyncDisposable
	{
		private readonly SqliteConnection _connection = new("DataSource=:memory:");
		public AppDbContext Db { get; }
		public MutableClock Clock { get; }
		public WeeklyReportReader Weekly { get; }
		public ReflectionReportReader Reflection { get; }
		public GeneralReportReader General { get; }
		public Fixture(string now)
		{
			_connection.Open();
			Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
			Db.Database.EnsureCreated();
			Clock = new(Utc(now));
			var factory = new TestPlannerDbContextFactory(_connection);
			var calendar = new PlanningCalendar(new WeeklyReviewStore(factory), Clock);
			Weekly = new(factory, Clock, calendar); Reflection = new(factory, Clock, calendar); General = new(factory, Clock, calendar);
		}
		public async Task Preferences(string zone, int weekStart = 1)
		{
			Db.WeeklyReviewPreferences.Add(new WeeklyReviewPreferencesRow { TimeZoneId = zone, WeekStart = weekStart, Enabled = false });
			await Db.SaveChangesAsync(TestContext.Current.CancellationToken);
		}
		public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
	}
}
