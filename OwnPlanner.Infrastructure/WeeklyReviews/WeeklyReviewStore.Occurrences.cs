using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Tasks;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

public sealed partial class WeeklyReviewStore
{
	public async Task<WeeklyReviewOccurrencePage> OccurrencesAsync(Guid reviewId, DateTime nowUtc, Guid? taskId = null,
		int offset = 0, int limit = 20, DateOnly? occurrenceDate = null, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var review = (await db.WeeklyReviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, ct))?.ToSnapshot()
			?? throw new KeyNotFoundException("Weekly review not found.");
		return (await ReadOccurrencesAsync(db, review, nowUtc, taskId, offset, limit, occurrenceDate, ct)).Page;
	}

	public async Task<T> ExecuteOccurrenceAsync<T>(Guid reviewId, Guid taskId,
		Func<WeeklyReviewState, IRecurringTaskTransaction, IPlanningWorkSnapshot, CancellationToken, Task<T>> transition,
		CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		// The review, source/list, facts and callback all share the writer reservation. No check
		// before this transaction authorizes a write after a concurrent expiry/replacement/action.
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var review = (await db.WeeklyReviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, ct))?.ToSnapshot()
			?? throw new KeyNotFoundException("Weekly review not found.");
		var task = await db.TaskItems.SingleOrDefaultAsync(t => t.Id == taskId, ct)
			?? throw new KeyNotFoundException("Task not found.");
		var available = await db.TaskLists.AnyAsync(l => l.Id == task.TaskListId && !l.IsArchived, ct);
		var result = await transition(review, new RecurringTaskStore.SeriesTransaction(db, task, available),
			new PlanningWorkStore.Snapshot(db, false), ct);
		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return result;
	}

	private sealed class SeriesWork
	{
		public int Pending { get; set; }
		public int Carryover { get; set; }
		public int Planned { get; set; }
	}

	private static async Task<(WeeklyReviewOccurrencePage Page, Dictionary<Guid, SeriesWork> Series)> ReadOccurrencesAsync(
		AppDbContext db, WeeklyReviewState review, DateTime nowUtc, Guid? taskId = null, int offset = 0, int limit = 20,
		DateOnly? occurrenceDate = null, CancellationToken ct = default)
	{
		var revisions = await db.TaskItems.AsNoTracking().Where(t => t.Recurrence != null && (!taskId.HasValue || t.Id == taskId))
			.Select(t => new { t.Id, t.UpdatedAt, t.RecurrenceRevision }).ToArrayAsync(ct);
		var series = new Dictionary<Guid, SeriesWork>();
		var page = await WeeklyReviewOccurrences.ReadAsync(new PlanningWorkStore.Snapshot(db, false), review, nowUtc,
			revisions.ToDictionary(t => t.Id, t => (t.UpdatedAt, t.RecurrenceRevision)), taskId, offset, limit, occurrenceDate,
			(item, source) =>
			{
				if (item.Status != PlanningWorkStatus.Pending) return;
				if (!series.TryGetValue(source.TaskId, out var work)) series[source.TaskId] = work = new();
				work.Pending = checked(work.Pending + 1);
				if (item.OccurrenceDate < review.TargetWeek) work.Carryover = checked(work.Carryover + 1);
				else work.Planned = checked(work.Planned + 1);
			}, ct);
		return (page, series);
	}
}
