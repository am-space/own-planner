using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

public sealed class WeeklyReviewStore(IPlannerDbContextFactory factory) : IWeeklyReviewStore
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
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var selection = new WeeklyReviewSelection(review, nowUtc);
		var focusStart = review.TargetWeek.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var focusEnd = review.TargetWeek.AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var active = db.TaskItems.AsNoTracking().Where(t => !t.IsCompleted && t.TrashedAt == null &&
			db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived));
		var remaining = active.Where(t =>
			!(t.GoalId != null && t.FocusAt >= focusStart && t.FocusAt < focusEnd &&
				db.Goals.Any(g => g.Id == t.GoalId && g.Status == GoalStatus.Active)));
		var carryover = await remaining.CountAsync(selection.Carryover, ct);
		var overdue = await remaining.CountAsync(selection.Overdue, ct);
		var upcoming = await remaining.CountAsync(selection.DueInTargetWeek, ct);
		var selected = remaining.Where(selection.Included);
		var total = await selected.CountAsync(ct);
		var page = await selected.OrderByDescending(selection.Overdue).ThenBy(t => t.Id).Skip(offset).Take(limit)
			.Select(t => new WeeklyReviewTaskRow(t.Id, t.Title, t.TaskListId, t.FocusAt, t.DueAt, t.UpdatedAt, t.GoalId)).ToListAsync(ct);
		return selection.BuildReport(carryover, overdue, upcoming, total, offset, limit, page);
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
		var completed = valid.Where(t => t.IsCompleted && t.CompletedAt >= completedStart && t.CompletedAt <= nowUtc);
		var planned = valid.Where(t => !t.IsCompleted && t.FocusAt >= focusStart && t.FocusAt < focusEnd);
		var completedTotal = await completed.CountAsync(ct);
		var plannedTotal = await planned.CountAsync(ct);
		var completedGoal = await completed.CountAsync(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value), ct);
		var plannedGoal = await planned.CountAsync(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value), ct);
		var linked = valid.Where(t => t.GoalId != null && activeGoalIds.Contains(t.GoalId.Value));
		var counts = await linked.GroupBy(t => t.GoalId!.Value).Select(group => new
		{
			GoalId = group.Key,
			CompletedLast7 = group.Count(t => t.IsCompleted && t.CompletedAt >= completedStart && t.CompletedAt <= nowUtc),
			CompletedLast14 = group.Count(t => t.IsCompleted && t.CompletedAt >= stalledStart && t.CompletedAt <= nowUtc),
			Open = group.Count(t => !t.IsCompleted),
			Planned = group.Count(t => !t.IsCompleted && t.FocusAt >= focusStart && t.FocusAt < focusEnd)
		}).ToDictionaryAsync(row => row.GoalId, ct);
		// SQLite's row_number keeps each goal's title samples bounded without a query per goal.
		var samples = await db.TaskItems.FromSqlInterpolated($"""
			SELECT * FROM (
				SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t."GoalId", t."IsCompleted" ORDER BY t."Id") AS "sampleRank"
				FROM "TaskItems" AS t
				JOIN "TaskLists" AS l ON l."Id" = t."TaskListId"
				JOIN "Goals" AS g ON g."Id" = t."GoalId"
				WHERE t."TrashedAt" IS NULL AND l."IsArchived" = 0 AND g."Status" = {GoalStatus.Active}
					AND (t."IsCompleted" = 0 OR (t."CompletedAt" >= {completedStart} AND t."CompletedAt" <= {nowUtc}))
			) WHERE "sampleRank" <= 10
			""").AsNoTracking()
			.Select(t => new { t.GoalId, t.IsCompleted, Task = new WeeklyReviewGoalTask(t.Id, t.Title, t.UpdatedAt, t.FocusAt) })
			.ToListAsync(ct);
		var recentByGoal = samples.Where(row => row.IsCompleted).ToLookup(row => row.GoalId!.Value, row => row.Task);
		var openByGoal = samples.Where(row => !row.IsCompleted).ToLookup(row => row.GoalId!.Value, row => row.Task);
		var rows = new List<WeeklyReviewGoalRow>(goals.Count);
		foreach (var goal in goals)
		{
			counts.TryGetValue(goal.Id, out var count);
			rows.Add(new(goal.Id, goal.Title, goal.CreatedAt, goal.Horizon, goal.TargetPeriod, goal.TargetDate,
				goal.Metric, goal.MetricCurrent, count?.CompletedLast7 ?? 0,
				recentByGoal[goal.Id].OrderBy(task => task.Id).ToArray(), count?.CompletedLast14 ?? 0,
				count?.Open ?? 0, openByGoal[goal.Id].OrderBy(task => task.Id).ToArray(), count?.Planned ?? 0, goal.LastResumedAt));
		}
		return new(rows, completedGoal, completedTotal - completedGoal, plannedGoal, plannedTotal - plannedGoal);
	}
}
