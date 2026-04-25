using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskListRepository(AppDbContext db)
	: RepositoryBase<TaskList, AppDbContext>(db), ITaskListRepository
{
	public async Task<IReadOnlyList<TaskList>> ListAsync(bool includeArchived, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		var query = Set.AsQueryable();
		if (!includeArchived)
			query = query.Where(tl => !tl.IsArchived);
		if (contextId.HasValue)
			query = query.Where(tl => tl.ContextId == contextId.Value);
		else if (excludeUnassigned)
			query = query.Where(tl => tl.ContextId != null);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct);
		return lists
			.OrderByDescending(tl => tl.UpdatedAt)
			.ToList();
	}

	public override async Task AddAsync(TaskList taskList, CancellationToken ct = default)
	{
		await Set.AddAsync(taskList, ct);
		try
		{
			await Db.SaveChangesAsync(ct);
		}
		catch (DbUpdateException)
		{
			var exists = await Set.AsNoTracking().AnyAsync(tl => tl.Id == taskList.Id, ct);
			if (!exists)
				throw;
			// Concurrent insert: another instance already created the same row; safe to ignore.
			Db.Entry(taskList).State = EntityState.Detached;
		}
	}
}
