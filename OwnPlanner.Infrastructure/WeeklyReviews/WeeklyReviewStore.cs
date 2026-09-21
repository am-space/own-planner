using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.WeeklyReviews;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

public sealed class WeeklyReviewStore(IPlannerDbContextFactory factory) : IWeeklyReviewStore
{
	public async Task<T> UpdateAsync<T>(Func<WeeklyReviewPreferences, IList<WeeklyReviewState>, T> transition, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		// SQLite's non-deferred transaction obtains the writer reservation before reading state.
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var preferences = await db.WeeklyReviewPreferences.SingleOrDefaultAsync(ct);
		if (preferences is null)
		{
			preferences = new WeeklyReviewPreferences();
			db.WeeklyReviewPreferences.Add(preferences);
		}
		var reviews = await db.WeeklyReviews.ToListAsync(ct);
		var known = reviews.Select(r => r.Id).ToHashSet();
		var result = transition(preferences, reviews);
		db.WeeklyReviews.AddRange(reviews.Where(r => !known.Contains(r.Id)));
		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return result;
	}

	public async Task<WeeklyReviewReport> ReportAsync(WeeklyReviewState review, DateTime nowUtc, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var selection = new WeeklyReviewSelection(review, nowUtc);
		var active = db.TaskItems.AsNoTracking().Where(t => !t.IsCompleted && t.TrashedAt == null &&
			db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived));
		var carryover = await active.CountAsync(selection.Carryover, ct);
		var overdue = await active.CountAsync(selection.Overdue, ct);
		var upcoming = await active.CountAsync(selection.DueInTargetWeek, ct);
		var selected = active.Where(selection.Included);
		var total = await selected.CountAsync(ct);
		var page = await selected.OrderByDescending(selection.Overdue).ThenBy(t => t.Id).Skip(offset).Take(limit)
			.Select(t => new WeeklyReviewTaskRow(t.Id, t.Title, t.TaskListId, t.FocusAt, t.DueAt, t.UpdatedAt)).ToListAsync(ct);
		return selection.BuildReport(carryover, overdue, upcoming, total, offset, limit, page);
	}
}
