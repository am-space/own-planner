using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Infrastructure.Reporting;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

public sealed partial class WeeklyReviewStore(IPlannerDbContextFactory factory) : IWeeklyReviewStore
{
	public async Task<WeeklyReviewPreferences> GetPreferencesAsync(CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		var row = await db.WeeklyReviewPreferences.AsNoTracking().SingleOrDefaultAsync(ct);
		return row?.ToSnapshot() ?? new WeeklyReviewPreferences();
	}

	public async Task<T> UpdateAsync<T>(Func<WeeklyReviewPreferences, IList<WeeklyReviewState>, T> transition, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		// SQLite's non-deferred transaction obtains the writer reservation before reading state.
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var preferencesRow = await db.WeeklyReviewPreferences.SingleOrDefaultAsync(ct);
		if (preferencesRow is null)
		{
			preferencesRow = new WeeklyReviewPreferencesRow();
			db.WeeklyReviewPreferences.Add(preferencesRow);
		}
		var preferences = preferencesRow.ToSnapshot();
		var rows = await db.WeeklyReviews.ToDictionaryAsync(r => r.Id, ct);
		var reviews = rows.Values.Select(r => r.ToSnapshot()).ToList();
		var result = transition(preferences, reviews);
		preferencesRow.Apply(preferences);
		foreach (var snapshot in reviews)
		{
			if (!rows.TryGetValue(snapshot.Id, out var row))
			{
				row = new WeeklyReviewRow { Id = snapshot.Id };
				db.WeeklyReviews.Add(row);
			}
			row.Apply(snapshot);
		}
		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return result;
	}

	public async Task<WeeklyReviewReport> ReportAsync(WeeklyReviewState review, DateTime nowUtc, int offset, int limit, CancellationToken ct = default)
	{
		if (offset < 0 || limit is < 1 or > 50) throw new ArgumentException("Use offset >= 0 and limit between 1 and 50.");
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var selection = new WeeklyReviewSelection(review, nowUtc);
		var focusStart = review.TargetWeek.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var focusEnd = review.TargetWeek.AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var activeGoalIds = await db.Goals.Where(g => g.Status == GoalStatus.Active).Select(g => g.Id).ToArrayAsync(ct);
		var ordinary = db.TaskItems.AsNoTracking().Where(t => t.Recurrence == null && !t.IsCompleted && t.TrashedAt == null &&
			db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived))
			.Where(t => !(t.GoalId != null && t.FocusAt >= focusStart && t.FocusAt < focusEnd && activeGoalIds.Contains(t.GoalId.Value)))
			.Where(selection.Included);
		var rows = await ordinary.Select(t => new WeeklyReviewTaskRow(t.Id, t.Title, t.TaskListId, t.FocusAt, t.DueAt, t.UpdatedAt, t.GoalId)).ToArrayAsync(ct);
		var sourceRows = selection.BuildReport(0, 0, 0, rows.Length, 0, rows.Length, rows).Tasks.ToList();
		var projection = await ReadOccurrencesAsync(db, review, nowUtc, ct: ct);
		var recurring = await db.TaskItems.AsNoTracking().Where(t => t.Recurrence != null && !t.IsCompleted && t.TrashedAt == null &&
			db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived))
			.Select(t => new WeeklyReviewTaskRow(t.Id, t.Title, t.TaskListId, t.FocusAt, t.DueAt, t.UpdatedAt, t.GoalId)).ToArrayAsync(ct);
		foreach (var row in recurring)
		{
			projection.Series.TryGetValue(row.Id, out var work);
			var source = selection.SeriesTask(row, work?.Pending ?? 0, work?.Carryover ?? 0, work?.Planned ?? 0,
				row.GoalId is { } goalId && activeGoalIds.Contains(goalId));
			if (source is not null) sourceRows.Add(source);
		}
		return new(nowUtc, sourceRows.Count(t => t.Carryover), sourceRows.Count(t => t.Overdue), sourceRows.Count(t => t.DueInTargetWeek),
			sourceRows.Count, offset, limit, sourceRows.OrderByDescending(t => t.Overdue).ThenBy(t => t.Id).Skip(offset).Take(limit).ToArray())
		{ Occurrences = projection.Page };
	}

	public async Task<IReadOnlyList<WeeklyReviewPausedGoal>> PausedGoalsAsync(CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		return await db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Paused && g.PausedAt != null)
			.OrderBy(g => g.Title).ThenBy(g => g.Id)
			.Select(g => new WeeklyReviewPausedGoal(g.Id, g.Title, g.PausedAt!.Value)).ToListAsync(ct);
	}

	public async Task<WeeklyReviewGoalData> GoalDataAsync(WeeklyReviewState review, DateTime nowUtc, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var goals = await db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Active)
			.Select(g => new { g.Id, g.Title, g.CreatedAt, g.LastResumedAt, g.Horizon, g.TargetPeriod, g.TargetDate, g.Metric, g.MetricCurrent })
			.ToListAsync(ct);
		var activeGoalIds = db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Active).Select(g => g.Id);
		var completedStart = nowUtc.AddDays(-7);
		var stalledStart = nowUtc.AddDays(-14);
		var focusStart = review.TargetWeek.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var focusEnd = review.TargetWeek.AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var valid = db.TaskItems.AsNoTracking().Where(t => t.TrashedAt == null &&
			db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived));
		var completed = valid.Where(t => t.Recurrence == null && t.IsCompleted && t.CompletedAt >= completedStart && t.CompletedAt <= nowUtc);
		var planned = valid.Where(t => t.Recurrence == null && !t.IsCompleted && t.FocusAt >= focusStart && t.FocusAt < focusEnd);
		var completedTotal = await completed.CountAsync(ct);
		var plannedTotal = await planned.CountAsync(ct);
		var completedGoal = await completed.CountAsync(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value), ct);
		var plannedGoal = await planned.CountAsync(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value), ct);
		var linked = valid.Where(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value));
		var counts = await linked.GroupBy(t => t.GoalId!.Value).Select(group => new
		{
			GoalId = group.Key,
			CompletedLast7 = group.Count(t => t.Recurrence == null && t.IsCompleted && t.CompletedAt >= completedStart && t.CompletedAt <= nowUtc),
			CompletedLast14 = group.Count(t => t.Recurrence == null && t.IsCompleted && t.CompletedAt >= stalledStart && t.CompletedAt <= nowUtc),
			Open = group.Count(t => !t.IsCompleted),
			Planned = group.Count(t => t.Recurrence == null && !t.IsCompleted && t.FocusAt >= focusStart && t.FocusAt < focusEnd)
		}).ToDictionaryAsync(row => row.GoalId, ct);
		// SQLite's row_number keeps each goal's title samples bounded without a query per goal.
		var samples = await db.TaskItems.FromSqlInterpolated($"""
			SELECT * FROM (
				SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t."GoalId", t."IsCompleted" ORDER BY t."Id") AS "sampleRank"
				FROM "TaskItems" AS t
				JOIN "TaskLists" AS l ON l."Id" = t."TaskListId"
				JOIN "Goals" AS g ON g."Id" = t."GoalId"
				WHERE t."TrashedAt" IS NULL AND l."IsArchived" = 0 AND g."Status" = {GoalStatus.Active}
					AND (t."IsCompleted" = 0 OR (t."Recurrence" IS NULL AND t."CompletedAt" >= {completedStart} AND t."CompletedAt" <= {nowUtc}))
			) WHERE "sampleRank" <= 10
			""").AsNoTracking()
			.Select(t => new { t.GoalId, t.IsCompleted, Task = new WeeklyReviewGoalTask(t.Id, t.Title, t.UpdatedAt, t.FocusAt) { IsRecurring = t.Recurrence != null } })
			.ToListAsync(ct);
		var recentByGoal = samples.Where(row => row.IsCompleted).ToLookup(row => row.GoalId!.Value, row => row.Task);
		var openByGoal = samples.Where(row => !row.IsCompleted).ToLookup(row => row.GoalId!.Value, row => row.Task);
		var calendar = WeeklyReviewOccurrences.Calendar(review, nowUtc);
		var range = new OccurrenceDateRange(review.TargetWeek, review.TargetWeek.AddDays(7));
		var work = await ReportWorkReader.ReadAsync(db, calendar, range,
			completedStart, nowUtc.AddTicks(1), 10, ct, pendingOccurrenceStart: WeeklyReviewOccurrences.Range(calendar).StartDate);
		var facts14 = await new PlanningWorkStore.Snapshot(db, false)
			.GetCompletionFactsAsync(stalledStart, nowUtc.AddTicks(1), null, ct);
		var sourceGoals = await db.TaskItems.AsNoTracking().Where(t => t.Recurrence != null)
			.Select(t => new { t.Id, t.GoalId }).ToDictionaryAsync(t => t.Id, t => t.GoalId, ct);
		var occurrenceActivity14 = facts14.Where(f => sourceGoals[f.Id.TaskId].HasValue)
			.GroupBy(f => sourceGoals[f.Id.TaskId]!.Value).ToDictionary(g => g.Key, g => g.Count());
		var projection = await ReadOccurrencesAsync(db, review, nowUtc, ct: ct);
		var plannedSeries = projection.Series.Where(s => s.Value.Planned > 0).ToArray();
		var plannedGoalSeries = plannedSeries.Count(s => sourceGoals[s.Key] is { } id && goals.Any(g => g.Id == id));
		plannedTotal += plannedSeries.Length;
		plannedGoal += plannedGoalSeries;
		var rows = new List<WeeklyReviewGoalRow>(goals.Count);
		foreach (var goal in goals)
		{
			counts.TryGetValue(goal.Id, out var count);
			rows.Add(new(goal.Id, goal.Title, goal.CreatedAt, goal.Horizon, goal.TargetPeriod, goal.TargetDate,
				goal.Metric, goal.MetricCurrent, count?.CompletedLast7 ?? 0,
				recentByGoal[goal.Id].OrderBy(task => task.Id).ToArray(), count?.CompletedLast14 ?? 0,
				count?.Open ?? 0, openByGoal[goal.Id].OrderBy(task => task.Id).ToArray(),
				(count?.Planned ?? 0) + plannedSeries.Count(s => sourceGoals[s.Key] == goal.Id), goal.LastResumedAt)
			{ Work = work.Goals.SingleOrDefault(g => g.Id == goal.Id),
				PlannedWork = new(count?.Planned ?? 0, plannedSeries.Count(s => sourceGoals[s.Key] == goal.Id),
					plannedSeries.Where(s => sourceGoals[s.Key] == goal.Id).Sum(s => s.Value.Planned)),
				OccurrenceCompletedLast14Count = occurrenceActivity14.GetValueOrDefault(goal.Id) });
		}
		var completedGoalCounts = work.Goals.Where(g => g.Id is { } id && goals.Any(goal => goal.Id == id))
			.Select(g => g.CompletionActivity.Counts).ToArray();
		var goalActivity = new PlanningWorkCounts(
			completedGoalCounts.Sum(c => c.OrdinaryTaskCount), completedGoalCounts.Sum(c => c.SeriesCount), completedGoalCounts.Sum(c => c.OccurrenceCount));
		var allActivity = work.CompletionActivity.Counts;
		var goalPlanned = plannedSeries.Where(s => sourceGoals[s.Key] is { } id && goals.Any(g => g.Id == id)).ToArray();
		var otherPlanned = plannedSeries.Except(goalPlanned).ToArray();
		return new(rows, completedGoal, completedTotal - completedGoal, plannedGoal, plannedTotal - plannedGoal)
		{
			Work = work,
			WorkShare = new(goalActivity, new(allActivity.OrdinaryTaskCount - goalActivity.OrdinaryTaskCount,
				allActivity.SeriesCount - goalActivity.SeriesCount, allActivity.OccurrenceCount - goalActivity.OccurrenceCount),
				new(plannedGoal - goalPlanned.Length, goalPlanned.Length, goalPlanned.Sum(s => s.Value.Planned)),
				new(plannedTotal - plannedGoal - otherPlanned.Length, otherPlanned.Length, otherPlanned.Sum(s => s.Value.Planned)))
		};
	}
}
