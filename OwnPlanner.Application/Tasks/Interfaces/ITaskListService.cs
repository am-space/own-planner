using OwnPlanner.Application.Tasks.DTOs;

namespace OwnPlanner.Application.Tasks.Interfaces;

public interface ITaskListService
{
	Task<TaskListDto> CreateAsync(string title, string? description = null, string? color = null, CancellationToken ct = default);
	Task<TaskListDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<TaskListDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
	Task<TaskListDto> UpdateAsync(Guid id, string? title = null, string? description = null, string? color = null, CancellationToken ct = default);
	Task ArchiveAsync(Guid id, CancellationToken ct = default);
	Task UnarchiveAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
