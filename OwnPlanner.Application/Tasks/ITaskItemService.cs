using OwnPlanner.Application.Common;

namespace OwnPlanner.Application.Tasks;

public interface ITaskItemService
{
	Task<TaskItemDto> CreateAsync(string title, Guid taskListId, string? description = null, DateTime? dueAt = null, bool isImportant = false, Guid? goalId = null, CancellationToken ct = default);
	Task<TaskItemDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItemDto>> ListAsync(bool includeCompleted = true, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItemDto>> ListByTaskListAsync(Guid taskListId, bool includeCompleted = true, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItemDto>> ListByGoalAsync(Guid goalId, bool includeCompleted = true, CancellationToken ct = default);
	Task<TaskItemDto> UpdateAsync(Guid id, string? title = null, string? description = null, DateTime? dueAt = null, bool? isImportant = null, Guid? goalId = null, bool clearGoalId = false, CancellationToken ct = default);
	Task AssignToListAsync(Guid taskId, Guid taskListId, CancellationToken ct = default);
	Task CompleteAsync(Guid id, CancellationToken ct = default);
	Task ReopenAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);

	Task SetFocusDateAsync(Guid id, DateTime? focusDateUtc, CancellationToken ct = default);
	Task ClearFocusDateAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItemDto>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted = false, CancellationToken ct = default);

	/// <summary>
	/// Paged list operations. <c>limit</c> is clamped to [1, 100] (0 or negative falls back to the
	/// default page size); <c>offset</c> is floored at 0. Results use the repository's deterministic
	/// ordering so offset paging is stable.
	/// </summary>
	Task<PagedResult<TaskItemDto>> ListPagedAsync(bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default);
	Task<PagedResult<TaskItemDto>> ListByTaskListPagedAsync(Guid taskListId, bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default);
	Task<PagedResult<TaskItemDto>> ListByGoalPagedAsync(Guid goalId, bool includeCompleted, int offset, int limit, CancellationToken ct = default);
	Task<PagedResult<TaskItemDto>> ListByFocusDatePagedAsync(DateTime focusDateUtc, bool includeCompleted, int offset, int limit, CancellationToken ct = default);
}
