using OwnPlanner.Application.Tasks.DTOs;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public class TaskListService(ITaskItemRepository repository) : ITaskListService
{
	private readonly ITaskItemRepository _repository = repository;

	public async Task<TaskItemDto> CreateAsync(string title, string? description = null, DateTime? dueAt = null, CancellationToken ct = default)
	{
		var item = new TaskItem(title, description, dueAt);
		await _repository.AddAsync(item, ct);
		return Map(item);
	}

	public async Task<TaskItemDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct);
		return item is null ? null : Map(item);
	}

	public async Task<IReadOnlyList<TaskItemDto>> ListAsync(bool includeCompleted = true, CancellationToken ct = default)
	{
		var items = await _repository.ListAsync(includeCompleted, ct);
		return items.Select(Map).ToList();
	}

	public async Task CompleteAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		item.Complete();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task ReopenAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		item.Reopen();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		await _repository.DeleteAsync(item, ct);
	}

	private static TaskItemDto Map(TaskItem item) => new(
		item.Id,
		item.Title,
		item.Description,
		item.IsCompleted,
		item.CreatedAt,
		item.UpdatedAt,
		item.DueAt,
		item.CompletedAt
	);
}
