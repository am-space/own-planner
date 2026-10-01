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

	public async Task<WeeklyReviewGoalData> GoalDataAsync(WeeklyReviewState review, DateTime nowUtc, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var goals = await db.Goals.AsNoTracking().Where(g => g.Status == GoalStatus.Active)
			.Select(g => new { g.Id, g.Title, g.CreatedAt, g.Horizon, g.TargetPeriod, g.TargetDate, g.Metric, g.MetricCurrent })
			.ToListAsync(ct);
		var activeIds = goals.Select(g => g.Id).ToArray();
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
		var completedGoal = await completed.CountAsync(t => t.GoalId != null && activeIds.Contains(t.GoalId.Value), ct);
		var plannedGoal = await planned.CountAsync(t => t.GoalId != null && activeIds.Contains(t.GoalId.Value), ct);
		var rows = new List<WeeklyReviewGoalRow>(goals.Count);
		foreach (var goal in goals)
		{
			var linked = valid.Where(t => t.GoalId == goal.Id);
			var recent = linked.Where(t => t.IsCompleted && t.CompletedAt >= completedStart && t.CompletedAt <= nowUtc);
			var open = linked.Where(t => !t.IsCompleted);
			var upcoming = open.Where(t => t.FocusAt >= focusStart && t.FocusAt < focusEnd);
			rows.Add(new(goal.Id, goal.Title, goal.CreatedAt, goal.Horizon, goal.TargetPeriod, goal.TargetDate,
				goal.Metric, goal.MetricCurrent, await recent.CountAsync(ct),
				await recent.OrderBy(t => t.Id).Take(10)
					.Select(t => new WeeklyReviewGoalTask(t.Id, t.Title, t.UpdatedAt, t.FocusAt)).ToListAsync(ct),
				await linked.CountAsync(t => t.IsCompleted && t.CompletedAt >= stalledStart && t.CompletedAt <= nowUtc, ct),
				await open.CountAsync(ct),
				await open.OrderBy(t => t.Id).Take(10)
					.Select(t => new WeeklyReviewGoalTask(t.Id, t.Title, t.UpdatedAt, t.FocusAt)).ToListAsync(ct),
				await upcoming.CountAsync(ct)));
		}
		return new(rows, completedGoal, completedTotal - completedGoal, plannedGoal, plannedTotal - plannedGoal);
	}
}
