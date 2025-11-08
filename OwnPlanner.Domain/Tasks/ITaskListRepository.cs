namespace OwnPlanner.Domain.Tasks;

public interface ITaskListRepository
{
	Task<TaskList?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskList>> ListAsync(bool includeArchived, CancellationToken ct = default);
	Task AddAsync(TaskList taskList, CancellationToken ct = default);
	Task UpdateAsync(TaskList taskList, CancellationToken ct = default);
	Task DeleteAsync(TaskList taskList, CancellationToken ct = default);
}
