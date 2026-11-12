using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Tasks;

public sealed class RecurringTaskStore(IPlannerDbContextFactory factory) : IRecurringTaskStore
{
	public async Task<IReadOnlyList<Guid>> ListOpenSeriesIdsAsync(CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		return await db.TaskItems.Where(t => t.Recurrence != null && !t.IsCompleted && t.TrashedAt == null)
			.OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(ct);
	}

	public async Task<T> ExecuteAsync<T>(Guid taskId, Func<IRecurringTaskTransaction, CancellationToken, Task<T>> work, CancellationToken ct = default)
	{
		await using var db = await factory.CreateAsync(ct);
		// SQLite begins a non-deferred write transaction: checks and writes are serialized across
		// independent connections/processes, not just within one service or host instance.
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var task = await db.TaskItems.SingleOrDefaultAsync(t => t.Id == taskId, ct)
			?? throw new KeyNotFoundException("Task not found.");
		var available = await db.TaskLists.AnyAsync(l => l.Id == task.TaskListId && !l.IsArchived, ct);
		var result = await work(new SeriesTransaction(db, task, available), ct);
		await db.SaveChangesAsync(ct);
		await transaction.CommitAsync(ct);
		return result;
	}

	private sealed class SeriesTransaction(AppDbContext db, TaskItem task, bool listAvailable) : IRecurringTaskTransaction
	{
		public TaskItem Task => task;
		public bool ListAvailable => listAvailable;

		public async Task<TaskOccurrenceFact?> GetFactAsync(DateOnly date, CancellationToken ct)
		{
			var row = await db.TaskOccurrenceFacts.FindAsync([task.Id, date], ct);
			return row?.ToFact();
		}

		public async Task PutFactAsync(TaskOccurrenceFact fact, CancellationToken ct)
		{
			if (fact.Id.TaskId != task.Id) throw new InvalidOperationException("Fact does not belong to this transaction.");
			var row = await db.TaskOccurrenceFacts.FindAsync([task.Id, fact.Id.OccurrenceDate], ct);
			if (row is null)
			{
				row = new() { TaskId = task.Id, OccurrenceDate = fact.Id.OccurrenceDate };
				db.TaskOccurrenceFacts.Add(row);
			}
			row.Status = fact.Status;
			row.CompletedAt = fact.CompletedAt;
		}
	}
}
