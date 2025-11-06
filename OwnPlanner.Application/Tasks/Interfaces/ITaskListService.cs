using OwnPlanner.Application.Tasks.DTOs;

namespace OwnPlanner.Application.Tasks.Interfaces;

public interface ITaskListService
{
	Task<TaskItemDto> CreateAsync(string title, string? description = null, DateTime? dueAt = null, CancellationToken ct = default);
	Task<TaskItemDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskItemDto>> ListAsync(bool includeCompleted = true, CancellationToken ct = default);
	Task CompleteAsync(Guid id, CancellationToken ct = default);
	Task ReopenAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
