using OwnPlanner.Application.Tasks.DTOs;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public class TaskItemService(ITaskItemRepository repository, ITaskListRepository taskListRepository) : ITaskItemService
{
	private readonly ITaskItemRepository _repository = repository;
	private readonly ITaskListRepository _taskListRepository = taskListRepository;

	public async Task<TaskItemDto> CreateAsync(string title, Guid taskListId, string? description = null, DateTime? dueAt = null, bool isImportant = false, CancellationToken ct = default)
	{
		// Validate that the task list exists
		var taskList = await _taskListRepository.GetAsync(taskListId, ct);
		if (taskList is null)
			throw new KeyNotFoundException($"TaskList {taskListId} not found");

		var item = new TaskItem(title, taskListId, description, dueAt, isImportant);
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

	public async Task<IReadOnlyList<TaskItemDto>> ListByTaskListAsync(Guid taskListId, bool includeCompleted = true, CancellationToken ct = default)
	{
		var items = await _repository.ListByTaskListAsync(taskListId, includeCompleted, ct);
		return items.Select(Map).ToList();
	}

	public async Task<TaskItemDto> UpdateAsync(Guid id, string? title = null, string? description = null, DateTime? dueAt = null, bool? isImportant = null, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		
		if (title is not null)
			item.SetTitle(title);
		if (description is not null)
			item.SetDescription(description);
		if (dueAt is not null)
			item.SetDueAt(dueAt);
		if (isImportant.HasValue)
			item.SetImportant(isImportant.Value);

		await _repository.UpdateAsync(item, ct);
		return Map(item);
	}

	public async Task AssignToListAsync(Guid taskId, Guid taskListId, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(taskId, ct) ?? throw new KeyNotFoundException($"Task {taskId} not found");
		
		// Validate that the task list exists
		var taskList = await _taskListRepository.GetAsync(taskListId, ct);
		if (taskList is null)
			throw new KeyNotFoundException($"TaskList {taskListId} not found");

		item.AssignToList(taskListId);
		await _repository.UpdateAsync(item, ct);
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
		item.IsImportant,
		item.CreatedAt,
		item.UpdatedAt,
		item.DueAt,
		item.CompletedAt,
		item.TaskListId
	);
}
