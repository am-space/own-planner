using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskListRepository(AppDbContext db) : ITaskListRepository
{
	private readonly AppDbContext _db = db;

	public async Task<TaskList?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.TaskLists.FirstOrDefaultAsync(tl => tl.Id == id, ct);

	public async Task<IReadOnlyList<TaskList>> ListAsync(bool includeArchived, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		var query = _db.TaskLists.AsQueryable();
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

	public async Task AddAsync(TaskList taskList, CancellationToken ct = default)
	{
		await _db.TaskLists.AddAsync(taskList, ct);
		try
		{
			await _db.SaveChangesAsync(ct);
		}
		catch (DbUpdateException)
		{
			var exists = await _db.TaskLists.AsNoTracking().AnyAsync(tl => tl.Id == taskList.Id, ct);
			if (!exists)
				throw;
			// Concurrent insert: another instance already created the same row; safe to ignore.
			_db.Entry(taskList).State = EntityState.Detached;
		}
	}

	public async Task UpdateAsync(TaskList taskList, CancellationToken ct = default)
	{
		_db.TaskLists.Update(taskList);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(TaskList taskList, CancellationToken ct = default)
	{
		_db.TaskLists.Remove(taskList);
		await _db.SaveChangesAsync(ct);
	}
}
