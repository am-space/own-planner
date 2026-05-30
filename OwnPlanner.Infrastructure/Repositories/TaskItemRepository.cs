using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskItemRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<TaskItem>(dbContextFactory), ITaskItemRepository
{
	public async Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.AsQueryable();
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items
			.OrderByDescending(t => t.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByTaskListAsync(Guid taskListId, bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TaskListId == taskListId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items
			.OrderByDescending(t => t.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.FocusAt.HasValue && t.FocusAt.Value.Date == focusDateUtc.Date);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByGoalAsync(Guid goalId, bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.GoalId == goalId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}
}
