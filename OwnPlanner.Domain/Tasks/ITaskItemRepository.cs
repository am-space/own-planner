namespace OwnPlanner.Domain.Tasks;

public interface ITaskItemRepository
{
	Task<TaskItem?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListByTaskListAsync(Guid taskListId, bool includeCompleted, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListByGoalAsync(Guid goalId, bool includeCompleted, CancellationToken ct = default);

	/// <summary>
	/// Returns a single page of tasks plus the total matching count, ordered deterministically:
	/// planned focus date ascending with unscheduled tasks last, then most-recently-updated, then id
	/// as a total-order tiebreaker so offset paging never skips or repeats rows.
	/// </summary>
	Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListPagedAsync(bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default);

	/// <summary>
	/// Returns a single page of tasks belonging to <paramref name="taskListId"/> plus the total
	/// matching count, using the same deterministic ordering as <see cref="ListPagedAsync"/>.
	/// </summary>
	Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByTaskListPagedAsync(Guid taskListId, bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default);

	/// <summary>
	/// Returns a single page of tasks planned for <paramref name="focusDateUtc"/> plus the total
	/// matching count, using the same deterministic ordering as <see cref="ListPagedAsync"/>.
	/// </summary>
	Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByFocusDatePagedAsync(DateTime focusDateUtc, bool includeCompleted, int offset, int limit, CancellationToken ct = default);

	/// <summary>
	/// Returns a single page of tasks linked to <paramref name="goalId"/> plus the total matching
	/// count, using the same deterministic ordering as <see cref="ListPagedAsync"/>.
	/// </summary>
	Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListByGoalPagedAsync(Guid goalId, bool includeCompleted, int offset, int limit, CancellationToken ct = default);

	Task AddAsync(TaskItem task, CancellationToken ct = default);
	Task UpdateAsync(TaskItem task, CancellationToken ct = default);
	Task DeleteAsync(TaskItem task, CancellationToken ct = default);
}
