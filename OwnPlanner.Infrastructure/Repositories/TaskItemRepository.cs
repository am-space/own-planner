using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskItemRepository(AppDbContext db)
	: RepositoryBase<TaskItem, AppDbContext>(db), ITaskItemRepository
{
	public async Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default)
	{
		var query = Set.AsQueryable();
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct);
		return items
			.OrderByDescending(t => t.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByTaskListAsync(Guid taskListId, bool includeCompleted, CancellationToken ct = default)
	{
		var query = Set.Where(t => t.TaskListId == taskListId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct);
		return items
			.OrderByDescending(t => t.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted, CancellationToken ct = default)
	{
		var query = Set.Where(t => t.FocusAt.HasValue && t.FocusAt.Value.Date == focusDateUtc.Date);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByGoalAsync(Guid goalId, bool includeCompleted, CancellationToken ct = default)
	{
		var query = Set.Where(t => t.GoalId == goalId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}
}
