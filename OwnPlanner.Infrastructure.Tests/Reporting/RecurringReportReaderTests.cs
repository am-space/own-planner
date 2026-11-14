using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Infrastructure.WeeklyReviews;
using OwnPlanner.Infrastructure.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public sealed class RecurringReportReaderTests : IAsyncLifetime
{
	private readonly SqliteConnection _connection = new("DataSource=:memory:");
	private readonly Clock _clock = new();
	private AppDbContext _db = null!;
	private IPlannerDbContextFactory Factory => new TestPlannerDbContextFactory(_connection);
	private IPlanningCalendar Calendar => new PlanningCalendar(new WeeklyReviewStore(Factory), _clock);
	private GeneralReportReader General => new(Factory, _clock, Calendar);
	private WeeklyReportReader Weekly => new(Factory, _clock, Calendar);
	private ReflectionReportReader Reflection => new(Factory, _clock, Calendar);
	private StrategicReportReader Strategic => new(Factory, Calendar);
	private RecurringTaskLifecycle Lifecycle => new(new RecurringTaskStore(Factory), Calendar, new RecurrenceCalculator());
	private CancellationToken Ct => TestContext.Current.CancellationToken;
	private TaskList _list = null!;
	private Goal _goal = null!;
	public async ValueTask InitializeAsync()
	{
		await _connection.OpenAsync(Ct);
		_db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
		await _db.Database.EnsureCreatedAsync(Ct);
		var context = new PlanningContext("Work", ContextType.Area);
		_list = new("Work", contextId: context.Id);
		_goal = new("Learn", GoalHorizon.Yearly, targetPeriod: "2026");
		_db.AddRange(context, _list, _goal); await _db.SaveChangesAsync(Ct);
	}
	public async ValueTask DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
	private static DateTime Utc(int day, int hour = 0) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);
	private async Task<TaskItem> Series(DateOnly? start = null, DateOnly? end = null,
		MissedOccurrencePolicy policy = MissedOccurrencePolicy.RetainEachObligation, RecurrenceRule? rule = null)
	{
		var task = new TaskItem(new string('x', 79) + "😀end", _list.Id, "PRIVATE description", Utc(15, 20), goalId: _goal.Id);
		task.ConfigureRecurrence(new(rule ?? RecurrenceRule.Weekly(1, [DayOfWeek.Friday, DayOfWeek.Sunday], DayOfWeek.Monday),
			start ?? new(2026, 11, 13), end ?? new(2026, 11, 15), policy));
		_db.Add(task); await _db.SaveChangesAsync(Ct); return task;
	}
	private async Task Fact(TaskItem task, int day, TaskOccurrenceStatus status, DateTime? completedAt = null)
	{
		_db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = new(2026, 11, day), Status = status, CompletedAt = completedAt });
		await _db.SaveChangesAsync(Ct);
	}
	private async Task<ReportWork> Read(string kind, int limit = 5) => kind switch
	{
		"general" => (await General.GetAsync(Ct)).Work!,
		"calendar" => (await General.GetCalendarAsync("thisWeek", Ct)).Calendar!.Work!,
		"attention" => (await General.GetAttentionAsync(new(Limit: limit), Ct)).Work!,
		"day" => (await General.GetDayAsync(new(Limit: limit), Ct)).Work!,
		"week" => (await Weekly.GetAsync(new(TaskSampleLimit: limit, CalendarPeriod: "thisWeek"), Ct)).Work!,
		"reflection" => (await Reflection.GetAsync(new(TaskSampleLimit: limit, CalendarPeriod: "thisWeek"), Ct)).Work!,
		_ => (await Strategic.GetAsync(new(TaskSampleLimit: limit), Ct)).Work!
	};

	[Theory]
	[InlineData("general", "UTC", 7)]
	[InlineData("calendar", "UTC", 7)]
	[InlineData("attention", "UTC", 7)]
	[InlineData("day", "UTC", 7)]
	[InlineData("week", "UTC", 7)]
	[InlineData("reflection", "UTC", 7)]
	[InlineData("strategic", "UTC", 7)]
	[InlineData("general", "America/Los_Angeles", 7)]
	[InlineData("calendar", "America/Los_Angeles", 7)]
	[InlineData("attention", "America/Los_Angeles", 7)]
	[InlineData("day", "America/Los_Angeles", 7)]
	[InlineData("week", "America/Los_Angeles", 7)]
	[InlineData("reflection", "America/Los_Angeles", 7)]
	[InlineData("strategic", "America/Los_Angeles", 7)]
	[InlineData("day", "UTC", 13)]
	public async Task EarlierFocusCannotHideAnInWindowOrdinaryDeadlineWhenOccurrenceRangeExpands(string kind, string zone, int focusDay)
	{
		_db.WeeklyReviewPreferences.Add(new() { Enabled = false, TimeZoneId = zone, WeekStart = 1 });
		await _db.SaveChangesAsync(Ct);
		var series = await Series();
		// General's root remains UTC; local reports must select November 15 even when UTC says November 16.
		var due = zone == "UTC" || kind == "general" ? Utc(15, 20) : Utc(16, 2);
		var ordinary = new TaskItem("Earlier focus, deadline in range", _list.Id, dueAt: due, goalId: _goal.Id);
		ordinary.SetFocusAt(Utc(focusDay));
		var outside = new TaskItem("Outside report", _list.Id, dueAt: Utc(7, 20)); outside.SetFocusAt(Utc(7));
		_db.AddRange(ordinary, outside); await _db.SaveChangesAsync(Ct);
		var work = await Read(kind);
		work.Pending.Counts.Should().Be(new PlanningWorkCounts(1, 1, 2));
		work.Scheduled.Counts.OrdinaryTaskCount.Should().Be(1);
		foreach (var section in new[] { work.Pending, work.Scheduled })
		{
			section.Samples.Should().ContainSingle(s => s.TaskId == ordinary.Id && s.OccurrenceDate == null && s.SelectionDate == new DateOnly(2026, 11, 15));
			section.Samples.Should().NotContain(s => s.TaskId == outside.Id);
		}
		work.Sources.Single(s => s.TaskId == ordinary.Id).FocusAt.Should().Be(Utc(focusDay));
		work.Sources.Single(s => s.TaskId == ordinary.Id).DueAt.Should().Be(due);
		work.Pending.Samples.Where(s => s.TaskId == series.Id).Select(s => s.OccurrenceDate)
			.Should().Equal(new DateOnly(2026, 11, 13), new(2026, 11, 15));
		work.Days.Single(d => d.Date == new DateOnly(2026, 11, 15)).Pending.Counts.OrdinaryTaskCount.Should().Be(1);
		if (kind is "week" or "reflection" or "strategic")
		{
			work.Goals.Single().Pending.Counts.Should().Be(work.Pending.Counts);
			work.Contexts.Single().Pending.Counts.Should().Be(work.Pending.Counts);
		}
		(await _db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}

	[Theory]
	[InlineData("general")]
	[InlineData("calendar")]
	[InlineData("attention")]
	[InlineData("day")]
	[InlineData("week")]
	[InlineData("reflection")]
	[InlineData("strategic")]
	public async Task EveryReportIncludesOrdinaryAndTwoDistinctOccurrencesWithExactUnitsAndBoundedMetadata(string kind)
	{
		var series = await Series();
		var ordinary = new TaskItem("Ordinary", _list.Id, goalId: _goal.Id, dueAt: Utc(15, 20)); ordinary.SetFocusAt(Utc(15));
		_db.Add(ordinary); await _db.SaveChangesAsync(Ct);
		var work = await Read(kind);
		work.Pending.Counts.Should().Be(new PlanningWorkCounts(1, 1, 2));
		work.Pending.TotalCount.Should().Be(3);
		work.Pending.Samples.Select(s => (s.TaskId, s.OccurrenceDate)).Should().OnlyHaveUniqueItems();
		work.Pending.Samples.Where(s => s.TaskId == series.Id).Select(s => s.OccurrenceDate).Should().Equal(new DateOnly(2026, 11, 13), new(2026, 11, 15));
		work.Pending.Samples.Where(s => s.TaskId == series.Id).Should().OnlyContain(s => s.Period != null && s.Title.Length == 79 && s.TitleTruncated);
		work.Sources.Should().HaveCount(2).And.OnlyHaveUniqueItems(s => s.TaskId);
		work.Sources.Single(s => s.TaskId == series.Id).DueAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
		if (kind is "week" or "reflection" or "strategic")
		{
			work.Goals.Single().Pending.TotalCount.Should().Be(3);
			work.Contexts.Single().Pending.TotalCount.Should().Be(3);
		}
		else { work.Goals.Should().BeEmpty(); work.Contexts.Should().BeEmpty(); }
		work.Pending.SampleLimit.Should().Be(5); work.Pending.Truncated.Should().BeFalse();
		JsonSerializer.Serialize(work).Should().NotContain("PRIVATE");
		(await _db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
		(await _db.TaskItems.CountAsync(Ct)).Should().Be(2);
	}

	[Theory]
	[InlineData("general")]
	[InlineData("calendar")]
	[InlineData("attention")]
	[InlineData("day")]
	[InlineData("week")]
	[InlineData("reflection")]
	[InlineData("strategic")]
	public async Task EveryReportCountsActualMondayActivityOnceAndNeverParentClosure(string kind)
	{
		_clock.Now = Utc(16, 12);
		var series = await Series(); series.Complete(); await _db.SaveChangesAsync(Ct);
		await Fact(series, 13, TaskOccurrenceStatus.Completed, Utc(16, 8));
		var ordinary = new TaskItem("Ordinary done", _list.Id, goalId: _goal.Id); ordinary.Complete(); _db.Add(ordinary); await _db.SaveChangesAsync(Ct);
		_db.Entry(ordinary).Property(t => t.CompletedAt).CurrentValue = Utc(16, 9); await _db.SaveChangesAsync(Ct);
		var work = await Read(kind);
		work.CompletionActivity.Counts.Should().Be(new PlanningWorkCounts(1, 1, 1));
		work.CompletionActivity.CompletedCount.Should().Be(2);
		work.CompletionActivity.Samples.Single(s => s.TaskId == series.Id).Should().Match<ReportWorkSample>(s => s.OccurrenceDate == new DateOnly(2026, 11, 13) && s.SelectionDate == new DateOnly(2026, 11, 16) && s.CompletedAt == Utc(16, 8));
		work.Scheduled.Samples.Should().NotContain(s => s.TaskId == series.Id);
		if (kind is "week" or "reflection" or "strategic") work.Goals.Single().CompletionActivity.CompletedCount.Should().Be(2);
		var friday = (await Reflection.GetAsync(new(CalendarPeriod: "lastWeek"), Ct)).Work!;
		friday.Scheduled.CompletedCount.Should().Be(1);
		friday.CompletionActivity.TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task CorrectionAfterAutomaticClosureRefreshesScheduledAndActivityCountsAndGoalEvidence()
	{
		_clock.Now = Utc(16, 12);
		var series = await Series(end: new(2026, 11, 13), policy: MissedOccurrencePolicy.SkipExpiredPeriods);
		await Lifecycle.ReconcileAsync(Ct);
		(await _db.TaskItems.AsNoTracking().SingleAsync(Ct)).IsCompleted.Should().BeTrue();
		var before = await Reflection.GetAsync(new(CalendarPeriod: "lastWeek"), Ct);
		before.Work!.Scheduled.SkippedCount.Should().Be(1); before.Work.CompletionActivity.CompletedCount.Should().Be(0);
		await Lifecycle.CompleteOccurrenceAsync(new(series.Id, new(2026, 11, 13)), Utc(16, 8), Ct);
		var after = await Reflection.GetAsync(new(CalendarPeriod: "lastWeek"), Ct);
		after.Work!.Scheduled.SkippedCount.Should().Be(0); after.Work.Scheduled.CompletedCount.Should().Be(1);
		after.Work.CompletionActivity.CompletedCount.Should().Be(0);
		var monday = await Reflection.GetAsync(new(CalendarPeriod: "thisWeek"), Ct);
		monday.Totals.CompletedTaskCount.Should().Be(0); // stored ordinary tasks only
		monday.Work!.CompletionActivity.CompletedCount.Should().Be(1);
		monday.Signals.ActiveGoalsWithoutCompletedWork.Should().NotContain(g => g.Id == _goal.Id);
		(await _db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(1);
		(await _db.TaskItems.AsNoTracking().SingleAsync(Ct)).IsCompleted.Should().BeTrue();
	}

	[Fact]
	public async Task UpcomingOccurrencesAreGoalPlanningEvidenceEvenWithNoFocusAndZeroSamples()
	{
		await Series();
		var report = await Weekly.GetAsync(new(TaskSampleLimit: 0, CalendarPeriod: "thisWeek"), Ct);
		report.Signals.ActiveGoalsWithoutFocusedWork.Should().NotContain(g => g.Id == _goal.Id);
		report.Work!.Pending.TotalCount.Should().Be(2); report.Work.Pending.Samples.Should().BeEmpty();
		report.Work.Pending.Truncated.Should().BeTrue(); report.Work.Sources.Should().BeEmpty();
		report.Work.Goals.Single().Scheduled.PendingCount.Should().Be(2);
		var strategic = await Strategic.GetAsync(new(TaskSampleLimit: 0), Ct);
		strategic.Totals.IncompleteTaskCount.Should().Be(1); // one stored series, two actionable occurrences
		strategic.Work!.Pending.Counts.Should().Be(new PlanningWorkCounts(0, 1, 2));
		strategic.Signals.ActiveGoalsWithoutActiveTasks.Should().NotContain(g => g.Id == _goal.Id);
	}

	[Theory]
	[InlineData("America/New_York", "2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z", 25)]
	[InlineData("America/New_York", "2026-03-08T05:00:00Z", "2026-03-09T04:00:00Z", 23)]
	public async Task ActivityUsesLocalDstHalfOpenInstantsAndOriginalScheduledDates(string zone, string first, string end, int hours)
	{
		var startUtc = DateTime.Parse(first, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
		var endUtc = DateTime.Parse(end, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
		_clock.Now = startUtc.AddHours(12);
		_db.WeeklyReviewPreferences.Add(new() { Enabled = false, TimeZoneId = zone, WeekStart = 1 }); await _db.SaveChangesAsync(Ct);
		var date = DateOnly.FromDateTime(_clock.Now);
		var series = await Series(start: date.AddDays(-1), end: date.AddDays(1), rule: RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday));
		foreach (var (offset, instant) in new[] { (-1, startUtc), (0, endUtc.AddTicks(-1)), (1, endUtc) })
		{
			_db.TaskOccurrenceFacts.Add(new() { TaskId = series.Id, OccurrenceDate = date.AddDays(offset), Status = TaskOccurrenceStatus.Completed, CompletedAt = instant });
		}
		await _db.SaveChangesAsync(Ct);
		var report = await General.GetDayAsync(new(), Ct);
		var work = report.Work!;
		(work.ActivityEndExclusiveUtc - work.ActivityStartUtc).TotalHours.Should().Be(hours);
		work.CompletionActivity.CompletedCount.Should().Be(2);
		work.CompletionActivity.Samples.Select(s => s.OccurrenceDate).Should().BeEquivalentTo(new[] { date.AddDays(-1), date });
		work.Scheduled.CompletedCount.Should().Be(1); work.Scheduled.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task SuccessorShorteningPreservesEarlyCompletedFactOutsideBoundsWithoutSatisfyingNewWork()
	{
		_clock.Now = Utc(16, 12);
		var series = await Series(end: new(2026, 11, 30));
		await Fact(series, 20, TaskOccurrenceStatus.Completed, Utc(16, 8));
		var successors = new RecurringTaskSuccessors(new RecurringTaskStore(Factory));
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, new(2026, 11, 16));
		var preview = await successors.PreviewAsync(series.Id, request, Ct);
		var result = await successors.ApplyAsync(series.Id, Guid.NewGuid(), request, preview.PreviewToken,
			SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		var work = (await Weekly.GetAsync(new(CalendarPeriod: "thisWeek"), Ct)).Work!;
		work.Scheduled.CompletedCount.Should().Be(1);
		work.Scheduled.Samples.Should().Contain(s => s.TaskId == series.Id && s.OccurrenceDate == new DateOnly(2026, 11, 20) && s.Status == PlanningWorkStatus.Completed);
		work.Pending.Samples.Should().Contain(s => s.TaskId == result.SuccessorTaskId && s.OccurrenceDate == new DateOnly(2026, 11, 20));
		work.CompletionActivity.CompletedCount.Should().Be(1);
		work.CompletionActivity.Samples.Should().ContainSingle(s => s.TaskId == series.Id && s.OccurrenceDate == new DateOnly(2026, 11, 20));
	}

	[Fact]
	public async Task CompleteOldRetainedPrefixHasExactCountsAndBoundedSamplesDaysAndSources()
	{
		await Series(start: new(1990, 1, 1), end: new(2026, 11, 15), rule: RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday));
		var report = await Weekly.GetAsync(new(TaskSampleLimit: 1, CalendarPeriod: "thisWeek"), Ct);
		var work = report.Work!;
		work.Pending.TotalCount.Should().Be(new DateOnly(2026, 11, 16).DayNumber - new DateOnly(1990, 1, 1).DayNumber);
		work.Pending.Samples.Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(1990, 1, 1));
		work.Pending.BeforeWindowCount.Should().Be(new DateOnly(2026, 11, 9).DayNumber - new DateOnly(1990, 1, 1).DayNumber);
		work.Days.Should().HaveCount(7); work.Sources.Should().ContainSingle();
		work.Pending.Truncated.Should().BeTrue();
		work.Goals.Single().Pending.TotalCount.Should().Be(work.Pending.TotalCount);
		(await _db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}

	[Theory]
	[InlineData(MissedOccurrencePolicy.SkipExpiredPeriods)]
	[InlineData(MissedOccurrencePolicy.RetainEachObligation)]
	public async Task EarlierMonthlyObligationRemainsActionableInDayReportWithoutBecomingOverdue(MissedOccurrencePolicy policy)
	{
		var series = await Series(start: new(2026, 11, 1), policy: policy, rule: RecurrenceRule.Monthly(1, [1]));
		var day = await General.GetDayAsync(new(), Ct);
		day.Work!.Pending.TotalCount.Should().Be(1);
		day.Work.Pending.Samples.Single().OccurrenceDate.Should().Be(new DateOnly(2026, 11, 1));
		day.Work.Scheduled.TotalCount.Should().Be(0);
		day.Work.Pending.BeforeWindowCount.Should().Be(1);
		// Only the explicitly stored source deadline can be overdue, never the occurrence date.
		day.Sections.Single(s => s.Name == "overdue").MatchCount.Should().Be(0);
	}

	[Theory]
	[InlineData("trash")]
	[InlineData("archive")]
	public async Task EveryWorkViewExcludesUnavailableSourcesAndTheirFacts(string hidden)
	{
		var series = await Series(); await Fact(series, 13, TaskOccurrenceStatus.Completed, Utc(15, 8));
		if (hidden == "trash") series.Trash(); else _list.Archive();
		await _db.SaveChangesAsync(Ct);
		foreach (var kind in new[] { "general", "calendar", "attention", "day", "week", "reflection", "strategic" })
		{
			var work = await Read(kind);
			work.Pending.TotalCount.Should().Be(0); work.Scheduled.TotalCount.Should().Be(0); work.CompletionActivity.TotalCount.Should().Be(0);
			work.Sources.Should().BeEmpty();
		}
	}
	[Fact]
	public async Task ParentWithManualFocusNeverAddsACompletionAndFarFutureLegacyWindowsRemainBounded()
	{
		var series = await Series(); series.SetFocusAt(Utc(15)); series.Complete(); await _db.SaveChangesAsync(Ct);
		var general = await General.GetCalendarAsync("thisWeek", Ct);
		general.Today.CompletedCount.Should().Be(0);
		general.Calendar!.Today.CompletedCount.Should().Be(0);
		general.Work!.CompletionActivity.TotalCount.Should().Be(0);
		var future = await Weekly.GetAsync(new(StartDate: new(2050, 1, 1)), Ct);
		future.Work!.ScheduledRange.Should().Be(new OccurrenceDateRange(new(2050, 1, 1), new(2050, 1, 8)));
		future.Work.Scheduled.TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task CancelledAndSkippedFactsTakePrecedenceOnceAndDoNotHideAnUnplannedGoal()
	{
		var series = await Series();
		await Fact(series, 13, TaskOccurrenceStatus.Cancelled);
		await Fact(series, 15, TaskOccurrenceStatus.Skipped);
		var report = await Weekly.GetAsync(new(CalendarPeriod: "thisWeek"), Ct);
		report.Work!.Scheduled.TotalCount.Should().Be(2);
		report.Work.Scheduled.Counts.SeriesCount.Should().Be(1);
		report.Work.Scheduled.CancelledCount.Should().Be(1);
		report.Work.Scheduled.SkippedCount.Should().Be(1);
		report.Work.Pending.TotalCount.Should().Be(0);
		report.Signals.ActiveGoalsWithoutFocusedWork.Should().Contain(g => g.Id == _goal.Id);
	}

	[Fact]
	public async Task CompactViewsStayBoundedAcrossManyGoalsContextsAndSeries()
	{
		for (var i = 0; i < 50; i++)
		{
			var context = new PlanningContext($"Context {i}", ContextType.Area);
			var list = new TaskList($"List {i}", contextId: context.Id);
			var goal = new Goal($"Goal {i}", GoalHorizon.Yearly, targetPeriod: "2026");
			var task = new TaskItem($"Series {i}", list.Id, goalId: goal.Id);
			task.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, [DayOfWeek.Sunday], DayOfWeek.Monday),
				new(2026, 11, 15), new(2026, 11, 15), MissedOccurrencePolicy.RetainEachObligation));
			_db.AddRange(context, list, goal, task);
		}
		await _db.SaveChangesAsync(Ct);
		foreach (var kind in new[] { "general", "calendar", "attention", "day" })
		{
			var work = await Read(kind);
			work.Pending.TotalCount.Should().Be(50); work.Pending.Truncated.Should().BeTrue();
			work.Pending.Samples.Should().HaveCount(5); work.Sources.Should().HaveCount(5);
			work.Goals.Should().BeEmpty(); work.Contexts.Should().BeEmpty();
			work.Days.Should().ContainSingle();
		}
	}

	private sealed class Clock : TimeProvider
	{
		public DateTime Now { get; set; } = Utc(15, 12);
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
}
