namespace OwnPlanner.Domain.Tasks;

public interface ITaskItemRepository
{
	Task<TaskItem?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListAsync(bool includeCompleted, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListByTaskListAsync(Guid taskListId, bool includeCompleted, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItem>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted, CancellationToken ct = default);
	Task AddAsync(TaskItem task, CancellationToken ct = default);
	Task UpdateAsync(TaskItem task, CancellationToken ct = default);
	Task DeleteAsync(TaskItem task, CancellationToken ct = default);
}
