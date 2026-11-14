using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Tasks;

public sealed class PlanningWorkStore(IPlannerDbContextFactory factory) : IPlanningWorkStore
{
	public async Task<T> ReadAsync<T>(Func<IPlanningWorkSnapshot, CancellationToken, Task<T>> read, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		// Match the existing report/successor snapshot pattern: checks, facts, totals and page
		// share one transaction. Nothing is tracked or saved, and expiry is never reconciled.
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var result = await read(new Snapshot(db), ct);
		await transaction.CommitAsync(ct);
		return result;
	}

	internal sealed class Snapshot(AppDbContext db, bool readDescriptions = true) : IPlanningWorkSnapshot
	{
		private IQueryable<TaskItem> AvailableTasks => db.TaskItems.AsNoTracking().Where(t => t.TrashedAt == null
			&& db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived));
		private IQueryable<TaskOccurrenceFactRow> AvailableFacts => db.TaskOccurrenceFacts.AsNoTracking()
			.Where(f => AvailableTasks.Any(t => t.Id == f.TaskId && t.Recurrence != null));

		public async Task<IReadOnlyList<PlanningWorkSource>> GetSourcesAsync(bool includeCompleted, CancellationToken ct)
		{
			var tasks = await AvailableTasks.Where(t => includeCompleted || !t.IsCompleted)
				.Select(t => new { t.Id, t.Title, PreviewText = readDescriptions ? t.Description : null, t.TaskListId,
					t.GoalId, t.IsImportant, t.IsCompleted, t.FocusAt, t.DueAt, t.Recurrence, t.RecurrencePredecessorTaskId, t.RecurrenceSuccessorTaskId })
				.ToListAsync(ct);
			var contexts = await db.TaskLists.AsNoTracking().Select(l => new { l.Id, l.ContextId }).ToDictionaryAsync(l => l.Id, l => l.ContextId, ct);
			return tasks.Select(t => new PlanningWorkSource(t.Id, t.Title, t.PreviewText, t.TaskListId, contexts[t.TaskListId],
				t.GoalId, t.IsImportant, t.IsCompleted, t.FocusAt,
				// SQLite stores this legacy instant without DateTime.Kind; keep the new read contract UTC.
				t.DueAt.HasValue ? DateTime.SpecifyKind(t.DueAt.Value, DateTimeKind.Utc) : null, t.Recurrence,
				t.RecurrencePredecessorTaskId, t.RecurrenceSuccessorTaskId)).ToArray();
		}

		public async Task<IReadOnlyList<TaskOccurrenceFact>> GetScheduledFactsAsync(OccurrenceDateRange range, Guid? taskId, CancellationToken ct)
		{
			var rows = await AvailableFacts.Where(f => (!taskId.HasValue || f.TaskId == taskId.Value)
				&& f.OccurrenceDate >= range.StartDate && f.OccurrenceDate < range.EndExclusiveDate).ToArrayAsync(ct);
			return rows.Select(r => r.ToFact()).ToArray();
		}

		public async Task<IReadOnlyList<TaskOccurrenceFact>> GetCompletionFactsAsync(DateTime startUtc, DateTime endExclusiveUtc, Guid? taskId, CancellationToken ct)
		{
			var rows = await AvailableFacts.Where(f => (!taskId.HasValue || f.TaskId == taskId.Value)
				&& f.Status == TaskOccurrenceStatus.Completed && f.CompletedAt >= startUtc && f.CompletedAt < endExclusiveUtc).ToArrayAsync(ct);
			return rows.Select(r => r.ToFact()).ToArray();
		}

		public async Task<TaskOccurrenceFact?> GetFactAsync(TaskOccurrenceId id, CancellationToken ct)
		{
			var row = await AvailableFacts.SingleOrDefaultAsync(f => f.TaskId == id.TaskId && f.OccurrenceDate == id.OccurrenceDate, ct);
			return row?.ToFact();
		}
	}
}
