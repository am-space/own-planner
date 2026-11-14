using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Goals;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tasks;

namespace OwnPlanner.Infrastructure.Tests.WeeklyReviews;

public sealed partial class WeeklyReviewTests
{
	private WeeklyReviewActions Actions => new(_store,
		new TaskItemService(new TaskItemRepository(_factory), new TaskListRepository(_factory)),
		new TaskListService(new TaskListRepository(_factory)), _clock, new GoalService(new GoalRepository(_factory)));
	private RecurringTaskLifecycle Lifecycle => new(new RecurringTaskStore(_factory), new PlanningCalendar(_store, _clock), new RecurrenceCalculator());
	private static DateOnly Sep(int day) => new(2026, 9, day);
	private async Task<TaskItem> SeedSeries(MissedOccurrencePolicy policy = MissedOccurrencePolicy.SkipExpiredPeriods,
		Guid? goalId = null, DateOnly? start = null, DateOnly? end = null, RecurrenceRule? rule = null, DateTime? dueAt = null)
	{
		var list = new TaskList("Series list");
		var task = new TaskItem("Weekly practice", list.Id, "PRIVATE RECURRENCE DESCRIPTION", dueAt, goalId: goalId);
		task.ConfigureRecurrence(new(rule ?? RecurrenceRule.Weekly(1, [DayOfWeek.Friday, DayOfWeek.Sunday], DayOfWeek.Monday),
			start ?? Sep(18), end, policy));
		await using var db = await _factory.CreateAsync(Ct);
		db.AddRange(list, task); await db.SaveChangesAsync(Ct); return task;
	}
	private async Task<Goal> SeedActiveGoal()
	{
		var goal = new Goal("Practice", GoalHorizon.Yearly, targetPeriod: "2026");
		await using var db = await _factory.CreateAsync(Ct); db.Add(goal); await db.SaveChangesAsync(Ct);
		db.Entry(goal).Property(g => g.CreatedAt).CurrentValue = _clock.Now.AddDays(-40); await db.SaveChangesAsync(Ct);
		return goal;
	}
	private async Task<WeeklyReviewActionResult> ApplyOccurrence(Guid reviewId, WeeklyReviewOccurrence row, string action = "completeOccurrence") =>
		await Actions.ApplyAsync(reviewId, row.Work.TaskId, row.Revision, action, ct: Ct,
			occurrenceDate: row.Work.OccurrenceDate, guardToken: row.GuardToken);
	private async Task PutFact(TaskItem task, DateOnly date, TaskOccurrenceStatus status, DateTime? completedAt = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = date, Status = status, CompletedAt = completedAt });
		await db.SaveChangesAsync(Ct);
	}

	[Theory]
	[InlineData(MissedOccurrencePolicy.SkipExpiredPeriods)]
	[InlineData(MissedOccurrencePolicy.RetainEachObligation)]
	public async Task SundayReviewKeepsCurrentAndNextOccurrencesActionableWithoutClosingSeries(MissedOccurrencePolicy policy)
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(policy, goal.Id);
		var view = await Service.OpenAsync(ct: Ct);
		view.Review.TargetWeek.Should().Be(Sep(21));
		var page = view.Report.Occurrences!;
		page.TotalCount.Should().Be(4); page.SeriesCount.Should().Be(1); page.CarryoverCount.Should().Be(2); page.PlannedCount.Should().Be(2);
		page.Items.Select(r => r.Work.OccurrenceDate).Should().Equal(Sep(18), Sep(20), Sep(25), Sep(27));
		page.Items.First().Work.Period!.EndsAtUtc.Should().Be(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));
		var row = view.Report.Tasks.Should().ContainSingle().Subject;
		row.IsRecurring.Should().BeTrue(); row.PendingOccurrenceCount.Should().Be(4); row.PlannedOccurrenceCount.Should().Be(2);
		row.Carryover.Should().BeTrue(); row.Overdue.Should().BeFalse();
		view.Report.Goals.UnplannedGoalIds.Should().NotContain(goal.Id);
		view.Report.Goals.Items.Single().PlannedTaskCount.Should().Be(1);
		view.Report.Goals.WorkShare!.PlannedGoal.Should().Be(new PlanningWorkCounts(0, 1, 2));
		(await ApplyOccurrence(view.Review.Id, page.Items[0])).Applied.Should().BeTrue();
		var fresh = await Service.OccurrencesAsync(view.Review.Id, series.Id, ct: Ct);
		fresh.TotalCount.Should().Be(3); fresh.Items.Should().Contain(r => r.Work.OccurrenceDate == Sep(25));
		await using var db = await _factory.CreateAsync(Ct);
		var parent = (await db.TaskItems.FindAsync([series.Id], Ct))!;
		parent.IsCompleted.Should().BeFalse(); parent.CompletedAt.Should().BeNull(); parent.Recurrence.Should().Be(series.Recurrence);
		var fact = await db.TaskOccurrenceFacts.SingleAsync(Ct);
		fact.OccurrenceDate.Should().Be(Sep(18)); fact.CompletedAt.Should().Be(_clock.Now);
	}

	[Fact]
	public async Task MixedSourceRowsAndOccurrencePagesHaveIndependentExactCountsAndNoDescriptionLeaks()
	{
		await Enable(); var series = await SeedSeries(dueAt: _clock.Now.AddDays(-1)); var ordinary = await Seed();
		var view = await Service.OpenAsync(limit: 1, ct: Ct);
		view.Report.TotalCount.Should().Be(2); view.Report.Tasks.Should().ContainSingle(t => t.Id == series.Id);
		view.Report.CarryoverCount.Should().Be(2); view.Report.OverdueCount.Should().Be(1);
		var identities = new List<(Guid, DateOnly?)>();
		for (var offset = 0; offset < 4; offset++)
		{
			var page = await Service.OccurrencesAsync(view.Review.Id, offset: offset, limit: 1, ct: Ct);
			page.TotalCount.Should().Be(4); page.HasMore.Should().Be(offset < 3);
			page.Sources.Should().ContainSingle(s => s.TaskId == series.Id && s.DueAt == series.DueAt);
			identities.Add((page.Items.Single().Work.TaskId, page.Items.Single().Work.OccurrenceDate));
		}
		identities.Should().OnlyHaveUniqueItems();
		(await Service.OpenAsync(view.Review.Id, offset: 1, limit: 1, ct: Ct)).Report.Tasks.Should().ContainSingle(t => t.Id == ordinary.Id);
		System.Text.Json.JsonSerializer.Serialize(view).Should().NotContain("PRIVATE RECURRENCE DESCRIPTION").And.NotContain("PRIVATE DESCRIPTION");
		await using var db = await _factory.CreateAsync(Ct); (await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}

	[Theory]
	[InlineData("complete")]
	[InlineData("focus")]
	[InlineData("goalFocus")]
	[InlineData("clearFocus")]
	[InlineData("trash")]
	[InlineData("deadline")]
	[InlineData("clearDeadline")]
	[InlineData("linkGoal")]
	public async Task OrdinaryReviewActionsCannotAccidentallyMutateSeries(string action)
	{
		await Enable(); var series = await SeedSeries(); var view = await Service.OpenAsync(ct: Ct);
		var result = await Actions.ApplyAsync(view.Review.Id, series.Id, series.UpdatedAt, action, Sep(22), _clock.Now.AddDays(2), Ct);
		result.Applied.Should().BeFalse(); result.Message.Should().Contain("recurring series");
		await using var db = await _factory.CreateAsync(Ct);
		var task = (await db.TaskItems.FindAsync([series.Id], Ct))!;
		task.IsCompleted.Should().BeFalse(); task.TrashedAt.Should().BeNull(); task.FocusAt.Should().BeNull(); task.DueAt.Should().BeNull();
	}

	[Theory]
	[InlineData("fact")]
	[InlineData("title")]
	[InlineData("archived")]
	[InlineData("trash")]
	[InlineData("closed")]
	[InlineData("review")]
	[InlineData("expiry")]
	[InlineData("replacement")]
	public async Task StaleOccurrenceGuardRejectsChangesWithoutWritingFacts(string change)
	{
		await Enable(); var series = await SeedSeries(); var view = await Service.OpenAsync(ct: Ct);
		var row = view.Report.Occurrences!.Items[0];
		if (change == "fact") await PutFact(series, Sep(18), TaskOccurrenceStatus.Cancelled);
		else if (change == "review") await Service.TransitionAsync(view.Review.Id, "complete", ct: Ct);
		else if (change == "expiry") _clock.Now = view.Review.StartsAtUtc;
		else if (change == "replacement")
		{
			var successors = new RecurringTaskSuccessors(new RecurringTaskStore(_factory));
			var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, Sep(21));
			var preview = await successors.PreviewAsync(series.Id, request, Ct);
			await successors.ApplyAsync(series.Id, Guid.NewGuid(), request, preview.PreviewToken, ct: Ct);
		}
		else
		{
			await using var db = await _factory.CreateAsync(Ct); var task = (await db.TaskItems.FindAsync([series.Id], Ct))!;
			if (change == "title") task.SetTitle("Changed");
			if (change == "archived") (await db.TaskLists.FindAsync([series.TaskListId], Ct))!.Archive();
			if (change == "trash") task.Trash();
			if (change == "closed") task.Complete();
			await db.SaveChangesAsync(Ct);
		}
		(await ApplyOccurrence(view.Review.Id, row)).Applied.Should().BeFalse();
		await using var verify = await _factory.CreateAsync(Ct);
		(await verify.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(change == "fact" ? 1 : 0);
		if (change == "expiry")
		{
			var refreshed = await Service.OccurrencesAsync(view.Review.Id, series.Id, occurrenceDate: Sep(18), ct: Ct);
			refreshed.TotalCount.Should().Be(0); // read-only calculated expiry, no fabricated pending fact
		}
	}

	[Fact]
	public async Task ConcurrentReviewActionsSerializeAndPreserveFirstCompletionTimestamp()
	{
		await Enable(); var series = await SeedSeries(); var view = await Service.OpenAsync(ct: Ct);
		var row = view.Report.Occurrences!.Items[0];
		var results = await Task.WhenAll(ApplyOccurrence(view.Review.Id, row), ApplyOccurrence(view.Review.Id, row));
		results.Count(r => r.Applied).Should().Be(1);
		(await ApplyOccurrence(view.Review.Id, row)).Applied.Should().BeFalse();
		await using var db = await _factory.CreateAsync(Ct);
		var fact = await db.TaskOccurrenceFacts.SingleAsync(Ct);
		fact.Status.Should().Be(TaskOccurrenceStatus.Completed); fact.CompletedAt.Should().Be(_clock.Now);
		(await db.TaskItems.FindAsync([series.Id], Ct))!.IsCompleted.Should().BeFalse();
	}

	[Fact]
	public async Task TargetWeekGoalPlanIsOmittedFromSourcePageAfterEarlierObligationsAreResolved()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id);
		await PutFact(series, Sep(18), TaskOccurrenceStatus.Completed, _clock.Now.AddDays(-2));
		await PutFact(series, Sep(20), TaskOccurrenceStatus.Cancelled);
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Tasks.Should().BeEmpty(); view.Report.Occurrences!.TotalCount.Should().Be(2);
		view.Report.Goals.Items.Single().OpenTasks.Should().ContainSingle(t => t.Id == series.Id && t.IsRecurring);
		view.Report.Goals.WorkShare!.CompletedGoal.Should().Be(new PlanningWorkCounts(0, 1, 1));
		view.Report.Goals.WorkShare.PlannedGoal.Should().Be(new PlanningWorkCounts(0, 1, 2));
	}

	[Fact]
	public async Task HistoricalSkippedCorrectionOnClosedPredecessorRefreshesProgressWithoutReopening()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id, end: Sep(18));
		_clock.Now = _clock.Now.AddDays(1); await Lifecycle.ReconcileAsync(Ct);
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Goals.Items.Single().Stalled.Should().BeTrue();
		view.Report.Goals.CompletedGoalWorkCount.Should().Be(0); view.Report.Goals.Work!.CompletionActivity.TotalCount.Should().Be(0);
		var details = await Service.OccurrencesAsync(view.Review.Id, series.Id, occurrenceDate: Sep(18), ct: Ct);
		details.Items.Single().Work.Status.Should().Be(PlanningWorkStatus.Skipped);
		(await ApplyOccurrence(view.Review.Id, details.Items.Single())).Applied.Should().BeTrue();
		var refreshed = await Service.OpenAsync(view.Review.Id, ct: Ct);
		refreshed.Report.Goals.WorkShare!.CompletedGoal.Should().Be(new PlanningWorkCounts(0, 1, 1));
		refreshed.Report.Goals.Items.Single().Work!.CompletionActivity.CompletedCount.Should().Be(1);
		refreshed.Report.Goals.Items.Single().Stalled.Should().BeFalse();
		refreshed.Report.Goals.CompletedGoalWorkCount.Should().Be(0); // legacy ordinary-task unit
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.FindAsync([series.Id], Ct))!.IsCompleted.Should().BeTrue();
		(await db.TaskOccurrenceFacts.SingleAsync(Ct)).OccurrenceDate.Should().Be(Sep(18));
	}

	[Theory]
	[InlineData("UTC")]
	[InlineData("America/New_York")]
	public async Task SundayMonthBoundaryAndFrozenPreferencesKeepEarlierPeriods(string zone)
	{
		_clock.Now = new DateTime(2026, 5, 31, 18, 0, 0, DateTimeKind.Utc); await Enable(zone);
		var weekly = await SeedSeries(start: new(2026, 5, 29));
		var monthly = await SeedSeries(start: new(2026, 5, 1), rule: RecurrenceRule.Monthly(1, [20]));
		var view = await Service.OpenAsync(ct: Ct); view.Review.TargetWeek.Should().Be(new DateOnly(2026, 6, 1));
		view.Report.Occurrences!.Items.Should().Contain(r => r.Work.TaskId == weekly.Id && r.Work.OccurrenceDate == new DateOnly(2026, 5, 29))
			.And.Contain(r => r.Work.TaskId == monthly.Id && r.Work.OccurrenceDate == new DateOnly(2026, 5, 20));
		view.Report.Goals.Work!.Pending.Samples.Should().Contain(r => r.TaskId == monthly.Id && r.OccurrenceDate == new DateOnly(2026, 5, 20));
		var row = view.Report.Occurrences.Items.First(r => r.Work.TaskId == weekly.Id);
		await Service.ConfigureAsync(false, "Pacific/Auckland", 0, "10:00", ct: Ct);
		var details = await Service.OccurrencesAsync(view.Review.Id, weekly.Id, occurrenceDate: row.Work.OccurrenceDate, ct: Ct);
		details.Calendar.TimeZone.Should().Be(zone); details.Calendar.WeekStart.Should().Be(1);
		details.Items.Single().GuardToken.Should().Be(row.GuardToken);
		(await ApplyOccurrence(view.Review.Id, row)).Applied.Should().BeTrue();
	}

	[Fact]
	public async Task CompletionActivityUsesActualInstantsAndExcludesAdministrativeClosureAndUnavailableSources()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id);
		await PutFact(series, Sep(18), TaskOccurrenceStatus.Completed, _clock.Now.AddDays(-7)); // inclusive activity start
		await PutFact(series, Sep(25), TaskOccurrenceStatus.Completed, _clock.Now); // completed early; inclusive report instant
		await PutFact(series, Sep(20), TaskOccurrenceStatus.Completed, _clock.Now.AddDays(-7).AddTicks(-1));
		var other = await SeedSeries(goalId: goal.Id); await PutFact(other, Sep(18), TaskOccurrenceStatus.Completed, _clock.Now);
		await using (var db = await _factory.CreateAsync(Ct))
		{
			(await db.TaskItems.FindAsync([series.Id], Ct))!.Complete();
			(await db.TaskLists.FindAsync([other.TaskListId], Ct))!.Archive(); await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Goals.WorkShare!.CompletedGoal.Should().Be(new PlanningWorkCounts(0, 1, 2));
		view.Report.Goals.Work!.CompletionActivity.Samples.Select(r => r.OccurrenceDate).Should().BeEquivalentTo([Sep(18), Sep(25)]);
		view.Report.Goals.Work.Scheduled.Samples.Should().ContainSingle(r => r.OccurrenceDate == Sep(25));
		view.Report.Goals.CompletedGoalWorkCount.Should().Be(0);
		view.Report.Goals.Items.Single().Stalled.Should().BeFalse();
	}

	[Theory]
	[InlineData("completeOccurrence", null, "token")]
	[InlineData("completeOccurrence", "2026-09-18", null)]
	[InlineData("focus", "2026-09-18", "token")]
	public async Task OccurrenceActionsRequireExplicitIdentityAndFreshGuard(string action, string? date, string? guard)
	{
		var apply = () => Actions.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), _clock.Now, action, ct: Ct,
			occurrenceDate: date is null ? null : DateOnly.Parse(date), guardToken: guard);
		await apply.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task RetainedObligationsRemainReachableBeyondFirstPageWithoutPersistingPendingRows()
	{
		await Enable(); var series = await SeedSeries(MissedOccurrencePolicy.RetainEachObligation, start: new(1990, 1, 1));
		var view = await Service.OpenAsync(ct: Ct);
		var total = view.Report.Occurrences!.TotalCount; total.Should().BeGreaterThan(3000);
		var last = await Service.OccurrencesAsync(view.Review.Id, series.Id, total - 1, 1, ct: Ct);
		last.TotalCount.Should().Be(total); last.Items.Single().Work.OccurrenceDate.Should().Be(Sep(27)); last.HasMore.Should().BeFalse();
		view.Report.Tasks.Should().ContainSingle(t => t.PendingOccurrenceCount == total);
		await using var db = await _factory.CreateAsync(Ct); (await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}
	[Fact]
	public async Task MixedGoalWorkShareCountsOrdinaryWorkAndOccurrencesWithSeparateSourceUnits()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id);
		var paused = await SeedPausedGoal(); var otherSeries = await SeedSeries(goalId: paused.Id);
		await PutFact(series, Sep(18), TaskOccurrenceStatus.Completed, _clock.Now);
		await PutFact(otherSeries, Sep(18), TaskOccurrenceStatus.Completed, _clock.Now);
		var ordinary = await Seed();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var task = (await db.TaskItems.FindAsync([ordinary.Id], Ct))!;
			task.SetGoalId(goal.Id); task.SetFocusAt(Sep(22).ToDateTime(TimeOnly.MinValue)); await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(ct: Ct); var share = view.Report.Goals.WorkShare!;
		share.PlannedGoal.Should().Be(new PlanningWorkCounts(1, 1, 2));
		share.PlannedOther.Should().Be(new PlanningWorkCounts(0, 1, 2));
		share.CompletedGoal.Should().Be(new PlanningWorkCounts(0, 1, 1));
		share.CompletedOther.Should().Be(new PlanningWorkCounts(0, 1, 1));
		view.Report.Goals.Items.Should().ContainSingle(g => g.Id == goal.Id && g.PlannedTaskCount == 2);
		view.Report.Goals.UnplannedGoalIds.Should().BeEmpty();
		view.Report.Goals.Items.Single().Stalled.Should().BeFalse();
		view.Report.Tasks.Should().Contain(t => t.Id == otherSeries.Id);
	}

	[Theory]
	[InlineData(3, 8, 167)]
	[InlineData(11, 1, 169)]
	public async Task FrozenReviewCalendarUsesFullDstWeekForOccurrenceExpiry(int month, int day, int hours)
	{
		_clock.Now = new DateTime(2026, month, day, 18, 0, 0, DateTimeKind.Utc); await Enable("America/New_York");
		var today = new DateOnly(2026, month, day); var friday = today.AddDays(-2);
		var series = await SeedSeries(start: friday);
		var view = await Service.OpenAsync(ct: Ct);
		var row = view.Report.Occurrences!.Items.First(r => r.Work.TaskId == series.Id);
		(row.Work.Period!.EndsAtUtc - row.Work.Period.StartsAtUtc).TotalHours.Should().Be(hours);
		row.Work.OccurrenceDate.Should().Be(friday);
		row.Work.Status.Should().Be(PlanningWorkStatus.Pending);
		(await ApplyOccurrence(view.Review.Id, row, "cancelOccurrence")).Applied.Should().BeTrue();
		var history = await Service.OccurrencesAsync(view.Review.Id, series.Id, occurrenceDate: friday, ct: Ct);
		history.Items.Single().Work.Status.Should().Be(PlanningWorkStatus.Cancelled);
		history.Items.Single().Work.CompletedAt.Should().BeNull();
		(await ApplyOccurrence(view.Review.Id, history.Items.Single())).Applied.Should().BeFalse();
	}

	[Theory]
	[InlineData(-1, 1)]
	[InlineData(0, 0)]
	[InlineData(0, 51)]
	public async Task OccurrencePagesValidateBoundsWithoutConsumingGuidance(int offset, int limit)
	{
		await Enable(); var goal = await SeedPausedGoal();
		var view = await Service.OpenAsync(ct: Ct, present: false);
		var read = () => Service.OccurrencesAsync(view.Review.Id, offset: offset, limit: limit, ct: Ct);
		await read.Should().ThrowAsync<ArgumentException>();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		(await Service.OpenAsync(view.Review.Id, ct: Ct)).PausedGoalsMention.Should().Contain(goal.Title);
	}

	[Fact]
	public async Task OccurrenceLookupDoesNotClaimGuidanceOrChangeRemindersAndExcludesUnavailableSeries()
	{
		await Enable(); var paused = await SeedPausedGoal(); var visible = await SeedSeries();
		var archived = await SeedSeries(); var trashed = await SeedSeries();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			(await db.TaskLists.FindAsync([archived.TaskListId], Ct))!.Archive();
			(await db.TaskItems.FindAsync([trashed.Id], Ct))!.Trash(); await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(ct: Ct, present: false);
		var details = await Service.OccurrencesAsync(view.Review.Id, ct: Ct);
		details.TotalCount.Should().Be(4); details.Sources.Should().ContainSingle(s => s.TaskId == visible.Id);
		(await Service.GetPreferencesAsync(Ct)).Enabled.Should().BeTrue();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		var claim = (await Service.ClaimReminderAsync(Ct))!;
		claim.Text.Should().NotContain(visible.Title).And.NotContain(paused.Title).And.NotContain(visible.Id.ToString());
		(await Service.OpenAsync(view.Review.Id, ct: Ct)).PausedGoalsMention.Should().Contain(paused.Title);
	}

	[Fact]
	public async Task FridayCompletedMondayUsesActivityInstantAndKeepsOriginalScheduledIdentity()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id);
		_clock.Now = new DateTime(2026, 9, 19, 18, 0, 0, DateTimeKind.Utc);
		var prior = await Service.OpenAsync(ct: Ct); prior.Review.TargetWeek.Should().Be(Sep(14));
		_clock.Now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
		await Lifecycle.CompleteOccurrenceAsync(new(series.Id, Sep(18)), ct: Ct);
		var current = await Service.OpenAsync(ct: Ct);
		current.Report.Goals.Items.Single().Work!.CompletionActivity.Samples.Should()
			.ContainSingle(s => s.TaskId == series.Id && s.OccurrenceDate == Sep(18) && s.CompletedAt == _clock.Now);
		current.Report.Goals.Work!.Scheduled.Samples.Should().NotContain(s => s.OccurrenceDate == Sep(18));
		var previous = await Service.OpenAsync(prior.Review.Id, ct: Ct);
		previous.Report.Goals.Work!.Scheduled.Samples.Should().ContainSingle(s => s.TaskId == series.Id &&
			s.OccurrenceDate == Sep(18) && s.Status == PlanningWorkStatus.Completed && s.CompletedAt == _clock.Now);
	}

	[Fact]
	public async Task SuccessorConflictChoicePreservesFutureFactAndInvalidatesOldReviewGuard()
	{
		await Enable(); var goal = await SeedActiveGoal(); var series = await SeedSeries(goalId: goal.Id);
		var before = await Service.OpenAsync(ct: Ct); var stale = before.Report.Occurrences!.Items[0];
		await PutFact(series, Sep(25), TaskOccurrenceStatus.Completed, _clock.Now);
		var successors = new RecurringTaskSuccessors(new RecurringTaskStore(_factory));
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Replace, Sep(21));
		var preview = await successors.PreviewAsync(series.Id, request, Ct);
		var result = await successors.ApplyAsync(series.Id, Guid.NewGuid(), request, preview.PreviewToken,
			SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		(await ApplyOccurrence(before.Review.Id, stale)).Applied.Should().BeFalse();
		var fresh = await Service.OpenAsync(before.Review.Id, ct: Ct);
		fresh.Report.Goals.WorkShare!.CompletedGoal.Should().Be(new PlanningWorkCounts(0, 1, 1));
		fresh.Report.Goals.WorkShare.PlannedGoal.Should().Be(new PlanningWorkCounts(0, 1, 2));
		var outcomes = fresh.Report.Goals.Work!.Scheduled.Samples.Where(s => s.OccurrenceDate == Sep(25)).ToArray();
		outcomes.Should().ContainSingle(s => s.TaskId == series.Id && s.Status == PlanningWorkStatus.Completed)
			.And.ContainSingle(s => s.TaskId == result.SuccessorTaskId && s.Status == PlanningWorkStatus.Pending);
		var old = await Service.OccurrencesAsync(before.Review.Id, series.Id, occurrenceDate: Sep(18), ct: Ct);
		(await ApplyOccurrence(before.Review.Id, old.Items.Single())).Applied.Should().BeTrue();
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.FindAsync([result.SuccessorTaskId], Ct))!.IsCompleted.Should().BeFalse();
		(await db.TaskOccurrenceFacts.SingleAsync(f => f.OccurrenceDate == Sep(25), Ct)).TaskId.Should().Be(series.Id);
	}

	[Fact]
	public async Task ReviewCompletionAcceptsExplicitActualTimeAndNeverMovesItOnRetry()
	{
		await Enable(); var series = await SeedSeries(); var view = await Service.OpenAsync(ct: Ct);
		var row = view.Report.Occurrences!.Items[0]; var actual = _clock.Now.AddDays(-3);
		var result = await Actions.ApplyAsync(view.Review.Id, series.Id, row.Revision, "completeOccurrence", ct: Ct,
			occurrenceDate: row.Work.OccurrenceDate, guardToken: row.GuardToken, completedAtUtc: actual);
		result.Applied.Should().BeTrue(); result.Occurrence!.CompletedAt.Should().Be(actual);
		_clock.Now = _clock.Now.AddDays(1);
		(await ApplyOccurrence(view.Review.Id, row)).Applied.Should().BeFalse();
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskOccurrenceFacts.SingleAsync(Ct)).CompletedAt.Should().Be(actual);
	}

	[Theory]
	[InlineData("future")]
	[InlineData("unspecified")]
	[InlineData("cancel")]
	public async Task InvalidReviewCompletionInstantsCannotWriteFacts(string kind)
	{
		await Enable(); var series = await SeedSeries(); var view = await Service.OpenAsync(ct: Ct);
		var row = view.Report.Occurrences!.Items[0];
		var instant = kind == "future" ? _clock.Now.AddTicks(1) : kind == "unspecified"
			? DateTime.SpecifyKind(_clock.Now, DateTimeKind.Unspecified) : _clock.Now;
		var apply = () => Actions.ApplyAsync(view.Review.Id, series.Id, row.Revision,
			kind == "cancel" ? "cancelOccurrence" : "completeOccurrence", ct: Ct,
			occurrenceDate: row.Work.OccurrenceDate, guardToken: row.GuardToken, completedAtUtc: instant);
		await apply.Should().ThrowAsync<ArgumentException>();
		await using var db = await _factory.CreateAsync(Ct); (await db.TaskOccurrenceFacts.CountAsync(Ct)).Should().Be(0);
	}

}
