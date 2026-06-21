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

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListPagedAsync(bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.AsQueryable();
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);
		if (onlyImportant)
			query = query.Where(t => t.IsImportant);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByTaskListPagedAsync(Guid taskListId, bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.TaskListId == taskListId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);
		if (onlyImportant)
			query = query.Where(t => t.IsImportant);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByFocusDatePagedAsync(DateTime focusDateUtc, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.FocusAt.HasValue && t.FocusAt.Value.Date == focusDateUtc.Date);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
	}

	public async Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByGoalPagedAsync(Guid goalId, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.TaskItems.Where(t => t.GoalId == goalId);
		if (!includeCompleted)
			query = query.Where(t => !t.IsCompleted);

		return await PageAsync(query, offset, limit, ct).ConfigureAwait(false);
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
