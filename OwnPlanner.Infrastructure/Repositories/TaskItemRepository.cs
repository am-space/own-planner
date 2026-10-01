using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Infrastructure.Repositories;

public class TaskItemRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<TaskItem>(dbContextFactory), ITaskItemRepository
{
	public async Task<TaskGoalLinkResult> LinkToActiveGoalAsync(Guid taskId, Guid goalId, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		// SQLite's non-deferred write transaction serializes eligibility checks, the conditional
		// update and its returned snapshot against competing planner writes.
		await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
		var updatedAt = DateTime.UtcNow;
		var changed = await db.TaskItems.Where(task => task.Id == taskId && task.TrashedAt == null && task.GoalId == null
			&& db.TaskLists.Any(list => list.Id == task.TaskListId && !list.IsArchived)
			&& db.Goals.Any(goal => goal.Id == goalId && goal.Status == GoalStatus.Active))
			.ExecuteUpdateAsync(setters => setters.SetProperty(task => task.GoalId, goalId)
				.SetProperty(task => task.UpdatedAt, updatedAt), ct).ConfigureAwait(false);
		var task = await db.TaskItems.AsNoTracking().SingleOrDefaultAsync(task => task.Id == taskId && task.TrashedAt == null, ct).ConfigureAwait(false);
		TaskGoalLinkResult result;
		if (changed == 1)
			result = new(TaskGoalLinkStatus.Linked, task!);
		else if (task is null)
			result = new(TaskGoalLinkStatus.TaskNotFound);
		else if (!await db.TaskLists.AnyAsync(list => list.Id == task.TaskListId && !list.IsArchived, ct).ConfigureAwait(false))
			result = new(TaskGoalLinkStatus.TaskListUnavailable);
		else
		{
			var goal = await db.Goals.AsNoTracking().SingleOrDefaultAsync(goal => goal.Id == goalId, ct).ConfigureAwait(false);
			result = goal is null ? new(TaskGoalLinkStatus.GoalNotFound)
				: goal.Status != GoalStatus.Active ? new(TaskGoalLinkStatus.GoalInactive)
				: task.GoalId == goalId ? new(TaskGoalLinkStatus.AlreadyLinked, task)
				: new(TaskGoalLinkStatus.ConflictingLink);
		}
		await transaction.CommitAsync(ct).ConfigureAwait(false);
		return result;
	}

	public new async Task<TaskItem?> GetAsync(Guid id, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		return await db.TaskItems.SingleOrDefaultAsync(t => t.Id == id && t.TrashedAt == null, ct).ConfigureAwait(false);
	}

	public async Task<TaskItem?> GetTrashedAsync(Guid id, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		return await db.TaskItems.SingleOrDefaultAsync(t => t.Id == id && t.TrashedAt != null, ct).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null);
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
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.TaskListId == taskListId);
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
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.FocusAt.HasValue && t.FocusAt.Value.Date == focusDateUtc.Date);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}

	public async Task<IReadOnlyList<TaskItem>> ListByGoalAsync(Guid goalId, bool includeCompleted, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.GoalId == goalId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		var items = await query.ToListAsync(ct).ConfigureAwait(false);
		return items.OrderByDescending(t => t.UpdatedAt).ToList();
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListPagedAsync(bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);
		if (onlyImportant)
			query = query.Where(t => t.IsImportant);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByTaskListPagedAsync(Guid taskListId, bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.TaskListId == taskListId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);
		if (onlyImportant)
			query = query.Where(t => t.IsImportant);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByFocusDatePagedAsync(DateTime focusDateUtc, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.FocusAt.HasValue && t.FocusAt.Value.Date == focusDateUtc.Date);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByGoalPagedAsync(Guid goalId, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt == null && t.GoalId == goalId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListTrashedPagedAsync(int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TrashedAt != null);
		var total = await query.CountAsync(ct).ConfigureAwait(false);
		var items = await query
			.OrderByDescending(t => t.TrashedAt)
			.ThenBy(t => t.Id)
			.Skip(offset)
			.Take(limit)
			.ToListAsync(ct)
			.ConfigureAwait(false);
		return (items, total);
	}

	public async Task<TaskRestoreResult> RestoreAsync(Guid id, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var restored = await db.TaskItems
			.Where(task => task.Id == id
				&& task.TrashedAt != null
				&& db.TaskLists.Any(list => list.Id == task.TaskListId))
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(task => task.TrashedAt, (DateTime?)null)
				.SetProperty(task => task.ActiveTaskListId, task => (Guid?)task.TaskListId), ct)
			.ConfigureAwait(false);
		if (restored == 1)
			return TaskRestoreResult.Restored;

		var state = await db.TaskItems
			.Where(task => task.Id == id && task.TrashedAt != null)
			.Select(task => new { OriginalListExists = db.TaskLists.Any(list => list.Id == task.TaskListId) })
			.SingleOrDefaultAsync(ct)
			.ConfigureAwait(false);
		return state is null ? TaskRestoreResult.TaskNotFound : TaskRestoreResult.OriginalTaskListNotFound;
	}

	public async Task<TaskPermanentDeleteResult> PermanentlyDeleteAsync(Guid id, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var deleted = await db.TaskItems
			.Where(task => task.Id == id && task.TrashedAt != null)
			.ExecuteDeleteAsync(ct)
			.ConfigureAwait(false);
		if (deleted == 1)
			return TaskPermanentDeleteResult.Deleted;

		return await db.TaskItems.AnyAsync(task => task.Id == id, ct).ConfigureAwait(false)
			? TaskPermanentDeleteResult.TaskNotTrashed
			: TaskPermanentDeleteResult.TaskNotFound;
	}

	/// <summary>
	/// Counts the filtered set, then returns one ordered page from it. Ordering and Skip/Take run in
	/// the database so paging stays bounded regardless of collection size. NULL focus dates sort last
	/// via the leading <c>FocusAt == null</c> key; <c>Id</c> is the final total-order tiebreaker.
	/// </summary>
	private static async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> PageAsync(IQueryable<TaskItem> filtered, int offset, int limit, CancellationToken ct)
	{
		var total = await filtered.CountAsync(ct).ConfigureAwait(false);
		var items = await filtered
			.OrderBy(t => t.FocusAt == null)
			.ThenBy(t => t.FocusAt)
			.ThenByDescending(t => t.UpdatedAt)
			.ThenBy(t => t.Id)
			.Skip(offset)
			.Take(limit)
			.ToListAsync(ct)
			.ConfigureAwait(false);
		return (items, total);
	}
}
