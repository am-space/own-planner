using FluentAssertions;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public sealed class PlanningWorkReaderTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ownplanner-work-reads-{Guid.NewGuid():N}");
	private readonly Clock _clock = new(new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc));
	private Factory _factory = null!;
	private CancellationToken Ct => TestContext.Current.CancellationToken;
	private static OccurrenceDateRange Window => new(new(2026, 11, 9), new(2026, 11, 23));
	private IPlanningCalendar Calendar => new PlanningCalendar(new WeeklyReviewStore(_factory), _clock);
	private PlanningWorkReader Reader => new(new PlanningWorkStore(_factory), Calendar, new RecurrenceCalculator());
	private TaskOccurrenceHistory History => new(new PlanningWorkStore(_factory), Calendar);

	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory); _factory = new(Path.Combine(_directory, "planner.db"));
		await using var db = await _factory.CreateAsync(Ct); await db.Database.MigrateAsync(Ct);
	}
	public ValueTask DisposeAsync() { Directory.Delete(_directory, true); return ValueTask.CompletedTask; }

	private async Task<TaskItem> Seed(string title = "Recurring", bool recurring = true,
		MissedOccurrencePolicy policy = MissedOccurrencePolicy.RetainEachObligation,
		DateOnly? start = null, DateOnly? end = null, RecurrenceRule? rule = null,
		bool important = false, DateTime? focus = null, DateTime? due = null, Guid? goal = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		var list = new TaskList("List", contextId: Guid.NewGuid());
		var task = new TaskItem(title, list.Id, "Private details", due, important, goal); task.SetFocusAt(focus);
		if (recurring) task.ConfigureRecurrence(new(rule ?? RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			start ?? new(2026, 11, 13), end, policy));
		db.AddRange(list, task); await db.SaveChangesAsync(Ct); return task;
	}
	private async Task Fact(TaskItem task, DateOnly date, TaskOccurrenceStatus status, DateTime? completed = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = date, Status = status, CompletedAt = completed });
		await db.SaveChangesAsync(Ct);
	}
	private async Task<PlanningWorkPage> Query(PlanningWorkQuery? query = null) => await Reader.QueryAsync(query ?? new(Window), Ct);
	private static DateTime Utc(int day, int hour = 0) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

	[Fact]
	public async Task MixedPagesHaveExactCountsStableTiesDistinctSourcesAndNoMissingIdentity()
	{
		var series = await Seed(due: Utc(14), focus: Utc(12));
		var ordinary = await Seed("Ordinary", recurring: false, focus: Utc(13), due: Utc(20));
		var undated = await Seed("Undated", recurring: false);
		var query = new PlanningWorkQuery(Window, OrdinaryTasks: OrdinaryTaskSelection.AllOpen, Limit: 1);
		var pages = new List<PlanningWorkItem>();
		for (var offset = 0; offset < 4; offset++)
		{
			var page = await Query(query with { Offset = offset }); pages.AddRange(page.Items);
			page.TotalCount.Should().Be(4); page.Counts.Should().Be(new PlanningWorkCounts(2, 1, 2));
			page.HasMore.Should().Be(offset < 3); page.Sources.Should().ContainSingle();
		}
		pages.Select(p => (p.TaskId, p.OccurrenceDate)).Should().OnlyHaveUniqueItems();
		pages.Select(p => (p.WorkDate?.DayNumber ?? int.MaxValue, p.TaskId, p.Kind)).Should().BeInAscendingOrder();
		pages.Last().TaskId.Should().Be(undated.Id);
		pages.Count(p => p.TaskId == ordinary.Id).Should().Be(1);
		var all = await Query(query with { Limit = 100 }); all.Items.Should().Equal(pages);
		all.Sources.Should().HaveCount(3); all.Sources.Single(s => s.TaskId == series.Id).DueAt.Should().Be(Utc(14));
		all.Sources.Single(s => s.TaskId == series.Id).DueAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
		all.Items.Where(i => i.Kind == PlanningWorkKind.Occurrence).Should().OnlyContain(i => i.OccurrenceDate == i.WorkDate && i.Period != null);
		var pastEnd = await Query(query with { Offset = int.MaxValue });
		pastEnd.Items.Should().BeEmpty(); pastEnd.TotalCount.Should().Be(4); pastEnd.HasMore.Should().BeFalse();
	}

	[Theory]
	[InlineData(OrdinaryTaskSelection.FocusInRange)]
	[InlineData(OrdinaryTaskSelection.DeadlineInRange)]
	[InlineData(OrdinaryTaskSelection.FocusOrDeadlineInRange)]
	public async Task OrdinaryRangeSelectionUsesCalendarFocusAndLocalDeadlineWithoutDuplicatingTasks(OrdinaryTaskSelection selection)
	{
		await new OwnPlanner.Application.WeeklyReviews.WeeklyReviewService(new WeeklyReviewStore(_factory), _clock)
			.ConfigureAsync(false, "America/Los_Angeles", 1, "18:00", ct: Ct);
		var due = new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc); // November 9 locally
		var both = await Seed(recurring: false, focus: Utc(9), due: due);
		await Seed(recurring: false, focus: Utc(8), due: Utc(9, 2)); // November 8 locally
		var page = await Query(new(new(new(2026, 11, 9), new(2026, 11, 10)), OrdinaryTasks: selection));
		page.Items.Should().ContainSingle().Which.TaskId.Should().Be(both.Id);
		page.Items[0].WorkDate.Should().Be(new DateOnly(2026, 11, 9));
		page.Calendar.TimeZone.Should().Be("America/Los_Angeles");
	}

	[Fact]
	public async Task FiltersRunOnTheCombinedRowsBeforeCountingAndPaging()
	{
		var goal = Guid.NewGuid(); var series = await Seed("Chosen series", important: true, goal: goal);
		await Seed("Chosen task", recurring: false, important: true, focus: Utc(13), goal: goal);
		await Seed("Other", important: true); await Seed("Chosen but unimportant");
		var page = await Query(new(Window, Search: " CHOSEN ", ImportantOnly: true, GoalId: goal, Offset: 1, Limit: 1));
		page.TotalCount.Should().Be(3); page.Counts.Should().Be(new PlanningWorkCounts(1, 1, 2));
		var onlySeries = await Query(new(Window, TaskId: series.Id, TaskListId: series.TaskListId, Kind: PlanningWorkKind.Occurrence));
		onlySeries.TotalCount.Should().Be(2); onlySeries.Counts.TaskCount.Should().Be(1);
		var context = onlySeries.Sources.Single().ContextId;
		(await Query(new(Window, ContextId: context))).TotalCount.Should().Be(2);
		(await Query(new(Window, ContextId: Guid.NewGuid()))).TotalCount.Should().Be(0);
		(await Query(new(Window, OrdinaryTasks: OrdinaryTaskSelection.None, Kind: PlanningWorkKind.OrdinaryTask))).TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task DecadesOfRetainedWorkStayReachableAcrossPagesWithoutPendingRows()
	{
		var first = new DateOnly(1990, 1, 1);
		var series = await Seed(start: first, rule: RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday));
		await Fact(series, first, TaskOccurrenceStatus.Cancelled);
		await Fact(series, first.AddDays(1), TaskOccurrenceStatus.Completed, Utc(14));
		var query = new PlanningWorkQuery(Window, IncludeRetainedBeforeStart: true, Limit: 3);
		var count = Window.EndExclusiveDate.DayNumber - first.DayNumber - 2;
		var firstPage = await Query(query); firstPage.TotalCount.Should().Be(count);
		firstPage.Items[0].OccurrenceDate.Should().Be(first.AddDays(2));
		var next = await Query(query with { Offset = 3 }); next.Items[0].OccurrenceDate.Should().Be(first.AddDays(5));
		var last = await Query(query with { Offset = count - 3 }); last.Items.Last().OccurrenceDate.Should().Be(Window.EndExclusiveDate.AddDays(-1));
		last.HasMore.Should().BeFalse();
		(await Query(query with { IncludeRetainedBeforeStart = false })).TotalCount.Should().Be(14);
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(2);
		(await db.TaskItems.SingleAsync(Ct)).RecurrenceCheckedBefore.Should().BeNull();
	}

	[Fact]
	public async Task PastRetainedFactsDoNotReappearEvenWithAllStatusesButFutureFactsRemainVisible()
	{
		var series = await Seed(start: new(2026, 10, 30));
		await Fact(series, new(2026, 10, 30), TaskOccurrenceStatus.Completed, Utc(14));
		await Fact(series, new(2026, 11, 6), TaskOccurrenceStatus.Cancelled);
		await Fact(series, new(2026, 11, 20), TaskOccurrenceStatus.Completed, Utc(14));
		var page = await Query(new(Window, IncludeRetainedBeforeStart: true, Status: null));
		page.TotalCount.Should().Be(2); page.Items.Last().Status.Should().Be(PlanningWorkStatus.Completed);
		(await Query(new(Window, IncludeRetainedBeforeStart: true))).TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task SundayShowsCurrentAndNextFridayAndReadsNeverReconcileExpiredHistory()
	{
		var series = await Seed(policy: MissedOccurrencePolicy.SkipExpiredPeriods, start: new(2020, 1, 1));
		var before = await new TaskItemRepository(_factory).GetAsync(series.Id, Ct);
		var sunday = await Query(); sunday.Items.Select(i => i.OccurrenceDate).Should().Equal(new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 20));
		_clock.Now = Utc(16);
		var monday = await Query(); monday.Items.Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(2026, 11, 20));
		var expired = await Query(new(Window, Status: PlanningWorkStatus.Skipped));
		expired.Items.Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(2026, 11, 13));
		(await Query(new(Window, IncludeRetainedBeforeStart: true))).TotalCount.Should().Be(1); // no skip-policy lookback
		(await new TaskItemRepository(_factory).GetAsync(series.Id, Ct)).Should().BeEquivalentTo(before);
		(await History.ListByScheduledDateAsync(Window, null, 0, 100, Ct)).TotalCount.Should().Be(0);
		await using var db = await _factory.CreateAsync(Ct); (await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}

	[Fact]
	public async Task MonthlyClampingAndFullPeriodExpiryAreSharedWithTheEngine()
	{
		_clock.Now = new(2027, 2, 28, 23, 59, 59, DateTimeKind.Utc);
		await Seed(policy: MissedOccurrencePolicy.SkipExpiredPeriods, start: new(2027, 2, 1), end: new(2027, 3, 31),
			rule: RecurrenceRule.Monthly(1, [29, 30, 31]));
		var query = new PlanningWorkQuery(new(new(2027, 2, 1), new(2027, 4, 1)));
		var page = await Query(query); page.TotalCount.Should().Be(4);
		page.Items[0].OccurrenceDate.Should().Be(new DateOnly(2027, 2, 28));
		page.Items[0].Period!.Dates.EndExclusiveDate.Should().Be(new DateOnly(2027, 3, 1));
		_clock.Now = new(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
		(await Query(query)).TotalCount.Should().Be(3);
		(await Query(query with { Status = PlanningWorkStatus.Skipped })).TotalCount.Should().Be(1);
	}

	[Theory]
	[InlineData("closed", 1)]
	[InlineData("trash", 0)]
	[InlineData("archive", 0)]
	public async Task ActiveExclusionsPreserveOnlyPermittedHistory(string state, int historicalCount)
	{
		var series = await Seed(); await Fact(series, new(2026, 11, 13), TaskOccurrenceStatus.Completed, Utc(14));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var task = await db.TaskItems.SingleAsync(Ct);
			if (state == "closed") task.Complete();
			if (state == "trash") task.Trash();
			if (state == "archive") (await db.TaskLists.SingleAsync(Ct)).Archive();
			await db.SaveChangesAsync(Ct);
		}
		(await Query(new(Window, Status: null, IncludeRetainedBeforeStart: true))).TotalCount.Should().Be(0);
		(await History.ListByScheduledDateAsync(Window, null, 0, 100, Ct)).TotalCount.Should().Be(historicalCount);
		(await History.ListByCompletionAsync(Utc(14), Utc(15), null, 0, 100, Ct)).TotalCount.Should().Be(historicalCount);
		(await History.GetAsync(new(series.Id, new(2026, 11, 13)), Ct) is not null).Should().Be(historicalCount == 1);
	}

	[Fact]
	public async Task ShortenedPredecessorFactsStayInHistoryAndAreNotRegeneratedAsActiveWork()
	{
		var series = await Seed(); var original = new TaskOccurrenceFact(new(series.Id, new(2026, 11, 20)), TaskOccurrenceStatus.Completed, Utc(14));
		await Fact(series, original.Id.OccurrenceDate, original.Status, original.CompletedAt);
		var successors = new RecurringTaskSuccessors(new RecurringTaskStore(_factory));
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, new(2026, 11, 16));
		var preview = await successors.PreviewAsync(series.Id, request, Ct);
		var result = await successors.ApplyAsync(series.Id, Guid.NewGuid(), request, preview.PreviewToken,
			SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		var active = await Query(new(Window, IncludeRetainedBeforeStart: true, Status: null));
		active.Items.Select(i => (i.TaskId, i.OccurrenceDate)).Should().BeEquivalentTo(new[]
		{
			(series.Id, (DateOnly?)new DateOnly(2026, 11, 13)), (result.SuccessorTaskId, (DateOnly?)new DateOnly(2026, 11, 20))
		});
		active.Items.Should().OnlyContain(i => i.Status == PlanningWorkStatus.Pending);
		(await History.GetAsync(original.Id, Ct)).Should().Be(original);
		var history = await History.QueryAsync(new(OccurrenceHistoryBasis.ScheduledDate, Window), Ct);
		history.Items.Should().ContainSingle().Which.Fact.Should().Be(original);
		history.Sources.Single().SuccessorTaskId.Should().Be(result.SuccessorTaskId);
		history.Sources.Single().Schedule!.EndDate.Should().Be(new DateOnly(2026, 11, 15));
	}

	[Fact]
	public async Task HistorySelectsOriginalDatesOrActualActivityWithExactHalfOpenPages()
	{
		var first = await Seed(); var second = await Seed();
		await Fact(first, new(2026, 11, 13), TaskOccurrenceStatus.Completed, Utc(16, 12)); // late
		await Fact(second, new(2026, 11, 20), TaskOccurrenceStatus.Completed, Utc(16, 12)); // early
		await Fact(first, new(2026, 11, 27), TaskOccurrenceStatus.Completed, Utc(17)); // exclusive boundary
		await Fact(first, new(2026, 11, 6), TaskOccurrenceStatus.Skipped);
		await Fact(second, new(2026, 11, 6), TaskOccurrenceStatus.Cancelled);
		var friday = await History.QueryAsync(new(OccurrenceHistoryBasis.ScheduledDate, new(new(2026, 11, 13), new(2026, 11, 14))), Ct);
		friday.TotalCount.Should().Be(1); friday.Items[0].Fact.CompletedAt.Should().Be(Utc(16, 12));
		var query = new OccurrenceHistoryQuery(OccurrenceHistoryBasis.CompletionTime, StartUtc: Utc(16), EndExclusiveUtc: Utc(17), Limit: 1);
		var a = await History.QueryAsync(query, Ct); var b = await History.QueryAsync(query with { Offset = 1 }, Ct);
		a.TotalCount.Should().Be(2); b.TotalCount.Should().Be(2); a.HasMore.Should().BeTrue(); b.HasMore.Should().BeFalse();
		new[] { a.Items[0].Fact.Id.TaskId, b.Items[0].Fact.Id.TaskId }.Should().BeInAscendingOrder();
		a.Selection.Basis.Should().Be(OccurrenceHistoryBasis.CompletionTime);
		var all = await History.ListByScheduledDateAsync(new(new(2026, 11, 1), new(2026, 12, 1)), null, 0, 100, Ct);
		all.TotalCount.Should().Be(5); all.Items.Select(f => f.Id).Should().OnlyHaveUniqueItems();
		all.Items.Select(f => (f.Id.OccurrenceDate, f.Id.TaskId)).Should().BeInAscendingOrder();
		(await History.ListByCompletionAsync(Utc(16), Utc(17), first.Id, 0, 100, Ct)).TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task HistoryIncludesRecordedSkipsButNeverSynthesizesUnrecordedExpectedDates()
	{
		var task = await Seed(policy: MissedOccurrencePolicy.SkipExpiredPeriods, start: new(2020, 1, 1), end: new(2026, 11, 13));
		await Fact(task, new(2026, 11, 6), TaskOccurrenceStatus.Skipped);
		await using (var db = await _factory.CreateAsync(Ct)) { (await db.TaskItems.SingleAsync(Ct)).Complete(); await db.SaveChangesAsync(Ct); }
		var page = await History.QueryAsync(new(OccurrenceHistoryBasis.ScheduledDate, Window), Ct); page.Items.Should().BeEmpty();
		var recorded = await History.ListByScheduledDateAsync(new(new(2026, 11, 1), new(2026, 11, 9)), null, 0, 100, Ct);
		recorded.Items.Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Skipped);
		(await Query()).Items.Should().BeEmpty();
	}

	[Fact]
	public async Task RecordedFactsNeedNotMatchTheRuleAndTerminalFactsOverrideComputedExpiry()
	{
		_clock.Now = Utc(23);
		var series = await Seed(policy: MissedOccurrencePolicy.SkipExpiredPeriods, start: new(2026, 11, 1));
		await Fact(series, new(2026, 11, 6), TaskOccurrenceStatus.Completed, Utc(16));
		await Fact(series, new(2026, 11, 13), TaskOccurrenceStatus.Cancelled);
		await Fact(series, new(2026, 11, 18), TaskOccurrenceStatus.Skipped); // preserved Wednesday fact, not a Friday
		var all = await Query(new(new(new(2026, 11, 1), new(2026, 11, 23)), Status: null));
		all.Items.Select(i => i.Status).Should().Equal(PlanningWorkStatus.Completed, PlanningWorkStatus.Cancelled, PlanningWorkStatus.Skipped);
		all.Items.Last().OccurrenceDate.Should().Be(new DateOnly(2026, 11, 20)); // computed skip
		var facts = await History.ListByScheduledDateAsync(Window, null, 0, 100, Ct);
		facts.TotalCount.Should().Be(2); facts.Items.Last().Id.OccurrenceDate.Should().Be(new DateOnly(2026, 11, 18));
		(await Query(new(Window, Status: PlanningWorkStatus.Completed))).TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task CompletionHistoryHonorsResolvedLocalDstBoundariesAndNeverCountsParentClosure()
	{
		var series = await Seed(start: new(2026, 10, 1));
		var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
		var start = CalendarRules.ToUtc(new(2026, 11, 1), TimeOnly.MinValue, zone);
		var end = CalendarRules.ToUtc(new(2026, 11, 2), TimeOnly.MinValue, zone);
		(end - start).TotalHours.Should().Be(25);
		await Fact(series, new(2026, 10, 23), TaskOccurrenceStatus.Completed, start.AddTicks(-1));
		await Fact(series, new(2026, 10, 30), TaskOccurrenceStatus.Completed, start);
		await Fact(series, new(2026, 11, 6), TaskOccurrenceStatus.Completed, end.AddTicks(-1));
		await Fact(series, new(2026, 11, 13), TaskOccurrenceStatus.Completed, end);
		await using (var db = await _factory.CreateAsync(Ct)) { (await db.TaskItems.SingleAsync(Ct)).Complete(); await db.SaveChangesAsync(Ct); }
		var calendar = new CountingCalendar(PlanningCalendarResolver.Resolve("today", _clock.Now, zone.Id, 0));
		var page = await new TaskOccurrenceHistory(new PlanningWorkStore(_factory), calendar)
			.QueryAsync(new(OccurrenceHistoryBasis.CompletionTime, StartUtc: start, EndExclusiveUtc: end), Ct);
		page.TotalCount.Should().Be(2); page.Items.Select(i => i.Fact.Id.OccurrenceDate)
			.Should().Equal(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 6));
		page.Sources.Single().IsCompleted.Should().BeTrue(); page.Calendar.WeekStart.Should().Be(0);
		calendar.Reads.Should().Be(1);
	}

	[Fact]
	public async Task EmptyRetiredGenerationHasNoWorkButPreservedFactsRemainReachable()
	{
		var task = await Seed(start: new(2026, 12, 1)); await Fact(task, new(2026, 12, 4), TaskOccurrenceStatus.Completed, Utc(14));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var old = await db.TaskItems.SingleAsync(Ct);
			db.Add(old.CreateRecurrenceSuccessor(new(old.Recurrence!.Rule, new(2026, 11, 16), null, old.Recurrence.MissedPolicy)));
			await db.SaveChangesAsync(Ct);
		}
		var range = new OccurrenceDateRange(new(2026, 11, 1), new(2027, 1, 1));
		(await Query(new(range, TaskId: task.Id, IncludeRetainedBeforeStart: true, Status: null))).TotalCount.Should().Be(0);
		var facts = await History.ListByScheduledDateAsync(range, task.Id, 0, 100, Ct);
		facts.Items.Should().ContainSingle().Which.Id.OccurrenceDate.Should().Be(new DateOnly(2026, 12, 4));
	}

	[Fact]
	public async Task UnrepresentableFullPeriodsAreRejectedExplicitlyAndFactsRemainAddressable()
	{
		var task = await Seed(start: new(9999, 12, 1), rule: RecurrenceRule.Monthly(1, [31]));
		await Fact(task, DateOnly.MaxValue, TaskOccurrenceStatus.Completed, Utc(14));
		var history = () => History.ListByCompletionAsync(Utc(14), Utc(15), task.Id, 0, 100, Ct);
		await history.Should().ThrowAsync<ArgumentOutOfRangeException>();
		(await History.GetAsync(new(task.Id, DateOnly.MaxValue), Ct)).Should().NotBeNull();
		var query = () => Query(new(new(new(9999, 12, 1), new(9999, 12, 31)), Status: null));
		// No eligible 31st lies in this half-open query; bounds are honored without date overflow.
		(await query()).TotalCount.Should().Be(0);
	}

	[Fact]
	public async Task ClockAndCalendarAreResolvedOnceAndDstPeriodsUseTheSameSnapshot()
	{
		_clock.Now = new(2026, 3, 8, 16, 0, 0, DateTimeKind.Utc);
		await Seed(policy: MissedOccurrencePolicy.SkipExpiredPeriods, start: new(2026, 3, 6),
			rule: RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday));
		var calendar = new CountingCalendar(PlanningCalendarResolver.Resolve("today", _clock.Now, "America/New_York", 1));
		var reader = new PlanningWorkReader(new PlanningWorkStore(_factory), calendar, new RecurrenceCalculator());
		var page = await reader.QueryAsync(new(new(new(2026, 3, 2), new(2026, 3, 16))), Ct);
		calendar.Reads.Should().Be(1); page.Calendar.Should().Be(calendar.Snapshot);
		page.Items.Should().HaveCount(2); (page.Items[0].Period!.EndsAtUtc - page.Items[0].Period!.StartsAtUtc).TotalHours.Should().Be(167);
		page.Items[0].OccurrenceDate.Should().Be(new DateOnly(2026, 3, 6));
		await Fact(await new TaskItemRepository(_factory).GetAsync(page.Items[0].TaskId, Ct) ?? throw new InvalidOperationException(),
			new(2026, 3, 6), TaskOccurrenceStatus.Completed, _clock.Now);
		var history = await new TaskOccurrenceHistory(new PlanningWorkStore(_factory), calendar)
			.QueryAsync(new(OccurrenceHistoryBasis.CompletionTime, StartUtc: _clock.Now, EndExclusiveUtc: _clock.Now.AddHours(1)), Ct);
		calendar.Reads.Should().Be(2); history.Calendar.Should().Be(calendar.Snapshot);
		history.Items.Single().Period.Should().Be(page.Items[0].Period);
	}

	[Fact]
	public async Task SnapshotReadsDoNotMixFactsWithAConcurrentParentMutation()
	{
		var task = await Seed(); await Fact(task, new(2026, 11, 13), TaskOccurrenceStatus.Cancelled);
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var attemptingWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writerFactory = new Factory(Path.Combine(_directory, "planner.db"), new WriteSignal(attemptingWrite));
		var writer = Task.Run(async () =>
		{
			await ready.Task.WaitAsync(Ct);
			await using var db = await writerFactory.CreateAsync(Ct);
			(await db.TaskItems.SingleAsync(Ct)).Complete();
			(await db.TaskOccurrenceFacts.SingleAsync(Ct)).Status = TaskOccurrenceStatus.Skipped;
			await db.SaveChangesAsync(Ct);
		}, Ct);
		await new PlanningWorkStore(_factory).ReadAsync(async (snapshot, token) =>
		{
			var before = await snapshot.GetSourcesAsync(false, token); ready.SetResult();
			await attemptingWrite.Task.WaitAsync(token);
			writer.IsCompleted.Should().BeFalse();
			var facts = await snapshot.GetScheduledFactsAsync(Window, null, token);
			var after = await snapshot.GetSourcesAsync(false, token);
			before.Should().ContainSingle(); after.Should().Equal(before);
			facts.Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Cancelled);
			return true;
		}, Ct);
		await writer;
		(await Query()).TotalCount.Should().Be(0);
		(await History.ListByScheduledDateAsync(Window, null, 0, 100, Ct)).Items.Single().Status.Should().Be(TaskOccurrenceStatus.Skipped);
	}

	private sealed class CountingCalendar(PlanningPeriod snapshot) : IPlanningCalendar
	{
		public PlanningPeriod Snapshot => snapshot;
		public int Reads { get; private set; }
		public Task<PlanningPeriod> ResolveAsync(string period, CancellationToken cancellationToken = default)
		{ Reads++; return Task.FromResult(snapshot); }
	}
	private sealed class Clock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
	private sealed class WriteSignal(TaskCompletionSource signal) : DbTransactionInterceptor
	{
		public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
			TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
		{
			signal.TrySetResult();
			return ValueTask.FromResult(result);
		}
	}
	private sealed class Factory(string path, IInterceptor? interceptor = null) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(
			new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False")
				.AddInterceptors(interceptor is null ? [] : [interceptor]).Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
