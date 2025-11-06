using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskItemRepository(AppDbContext db) : ITaskItemRepository
{
	private readonly AppDbContext _db = db;

	public async Task<TaskItem?> GetAsync(Guid id, CancellationToken ct = default)
	=> await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == id, ct);

	public async Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default)
	{
		var query = _db.TaskItems.AsQueryable();
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var items = await query.ToListAsync(ct);
		return items
			.OrderByDescending(t => t.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(TaskItem task, CancellationToken ct = default)
	{
		await _db.TaskItems.AddAsync(task, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(TaskItem task, CancellationToken ct = default)
	{
		_db.TaskItems.Update(task);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(TaskItem task, CancellationToken ct = default)
	{
		_db.TaskItems.Remove(task);
		await _db.SaveChangesAsync(ct);
	}
}
