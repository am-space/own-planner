using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskListRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<TaskList>(dbContextFactory), ITaskListRepository
{
	public async Task<IReadOnlyList<TaskList>> ListAsync(bool includeArchived, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskLists.AsQueryable();
		if (!includeArchived)
			query = query.Where(tl => !tl.IsArchived);
		if (contextId.HasValue)
			query = query.Where(tl => tl.ContextId == contextId.Value);
		else if (excludeUnassigned)
			query = query.Where(tl => tl.ContextId != null);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct).ConfigureAwait(false);
		return lists
			.OrderByDescending(tl => tl.UpdatedAt)
			.ToList();
	}

	public override async Task AddAsync(TaskList taskList, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var set = db.TaskLists;
		await set.AddAsync(taskList, ct).ConfigureAwait(false);
		try
		{
			await db.SaveChangesAsync(ct).ConfigureAwait(false);
		}
		catch (DbUpdateException)
		{
			var exists = await set.AsNoTracking().AnyAsync(tl => tl.Id == taskList.Id, ct).ConfigureAwait(false);
			if (!exists)
				throw;
			// Concurrent insert: another instance already created the same row; safe to ignore.
			db.Entry(taskList).State = EntityState.Detached;
		}
	}
}
