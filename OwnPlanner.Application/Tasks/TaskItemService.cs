using OwnPlanner.Application.Common;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public class TaskItemService(ITaskItemRepository repository, ITaskListRepository taskListRepository) : ITaskItemService
{
	/// <summary>Default page size when a caller does not specify a positive limit.</summary>
	public const int DefaultPageLimit = 25;

	/// <summary>Hard upper bound on page size, so a single page can never grow unbounded.</summary>
	public const int MaxPageLimit = 100;

	private readonly ITaskItemRepository _repository = repository;
	private readonly ITaskListRepository _taskListRepository = taskListRepository;

	public async Task<TaskItemDto> CreateAsync(string title, Guid taskListId, string? description = null, DateTime? dueAt = null, bool isImportant = false, Guid? goalId = null, CancellationToken ct = default)
	{
		// Validate that the task list exists
		var taskList = await _taskListRepository.GetAsync(taskListId, ct);
		if (taskList is null)
			throw new KeyNotFoundException($"TaskList {taskListId} not found");

		var item = new TaskItem(title, taskListId, description, dueAt, isImportant, goalId);
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

	public async Task<TaskItemDto> UpdateAsync(Guid id, string? title = null, string? description = null, DateTime? dueAt = null, bool? isImportant = null, Guid? goalId = null, bool clearGoalId = false, CancellationToken ct = default)
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
		if (clearGoalId)
			item.SetGoalId(null);
		else if (goalId.HasValue)
			item.SetGoalId(goalId.Value);

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
		var item = await _repository.GetAsync(id, ct) ?? await _repository.GetTrashedAsync(id, ct)
			?? throw new KeyNotFoundException($"Task {id} not found");
		item.Trash();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task<PagedResult<TrashedTaskItemDto>> ListTrashedPagedAsync(int offset, int limit, CancellationToken ct = default)
	{
		var (o, l) = Normalize(offset, limit);
		var (items, total) = await _repository.ListTrashedPagedAsync(o, l, ct);
		return new PagedResult<TrashedTaskItemDto>(items.Select(MapTrashed).ToList(), total, o, l);
	}

	public async Task RestoreAsync(Guid id, CancellationToken ct = default)
	{
		switch (await _repository.RestoreAsync(id, ct))
		{
			case TaskRestoreResult.Restored:
				return;
			case TaskRestoreResult.TaskNotFound:
				throw new KeyNotFoundException($"Trashed task {id} not found");
			case TaskRestoreResult.OriginalTaskListNotFound:
				throw new InvalidOperationException("The task cannot be restored because its original task list no longer exists.");
			default:
				throw new InvalidOperationException("Unexpected task restore result.");
		}
	}

	public async Task PermanentlyDeleteAsync(Guid id, CancellationToken ct = default)
	{
		switch (await _repository.PermanentlyDeleteAsync(id, ct))
		{
			case TaskPermanentDeleteResult.Deleted:
				return;
			case TaskPermanentDeleteResult.TaskNotFound:
				throw new KeyNotFoundException($"Task {id} not found");
			case TaskPermanentDeleteResult.TaskNotTrashed:
				throw new InvalidOperationException("Only a task already in Trash can be permanently deleted.");
			default:
				throw new InvalidOperationException("Unexpected permanent-delete result.");
		}
	}

	public async Task SetFocusDateAsync(Guid id, DateTime? focusDateUtc, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		item.SetFocusAt(focusDateUtc);
		await _repository.UpdateAsync(item, ct);
	}

	public async Task ClearFocusDateAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Task {id} not found");
		item.ClearFocusAt();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task<IReadOnlyList<TaskItemDto>> ListByFocusDateAsync(DateTime focusDateUtc, bool includeCompleted = false, CancellationToken ct = default)
	{
		var items = await _repository.ListByFocusDateAsync(focusDateUtc, includeCompleted, ct);
		return items.Select(Map).ToList();
	}

	public async Task<IReadOnlyList<TaskItemDto>> ListByGoalAsync(Guid goalId, bool includeCompleted = true, CancellationToken ct = default)
	{
		var items = await _repository.ListByGoalAsync(goalId, includeCompleted, ct);
		return items.Select(Map).ToList();
	}

	public async Task<PagedResult<TaskItemDto>> ListPagedAsync(bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		var (o, l) = Normalize(offset, limit);
		var (items, total) = await _repository.ListPagedAsync(includeCompleted, onlyImportant, o, l, ct);
		return ToPage(items, total, o, l);
	}

	public async Task<PagedResult<TaskItemDto>> ListByTaskListPagedAsync(Guid taskListId, bool includeCompleted, bool onlyImportant, int offset, int limit, CancellationToken ct = default)
	{
		var (o, l) = Normalize(offset, limit);
		var (items, total) = await _repository.ListByTaskListPagedAsync(taskListId, includeCompleted, onlyImportant, o, l, ct);
		return ToPage(items, total, o, l);
	}

	public async Task<PagedResult<TaskItemDto>> ListByGoalPagedAsync(Guid goalId, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		var (o, l) = Normalize(offset, limit);
		var (items, total) = await _repository.ListByGoalPagedAsync(goalId, includeCompleted, o, l, ct);
		return ToPage(items, total, o, l);
	}

	public async Task<PagedResult<TaskItemDto>> ListByFocusDatePagedAsync(DateTime focusDateUtc, bool includeCompleted, int offset, int limit, CancellationToken ct = default)
	{
		var (o, l) = Normalize(offset, limit);
		var (items, total) = await _repository.ListByFocusDatePagedAsync(focusDateUtc, includeCompleted, o, l, ct);
		return ToPage(items, total, o, l);
	}

	private static (int Offset, int Limit) Normalize(int offset, int limit) => (
		Math.Max(0, offset),
		Math.Clamp(limit <= 0 ? DefaultPageLimit : limit, 1, MaxPageLimit));

	private static PagedResult<TaskItemDto> ToPage(IReadOnlyList<TaskItem> items, int total, int offset, int limit)
		=> new(items.Select(Map).ToList(), total, offset, limit);

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
		item.TaskListId,
		item.FocusAt, // My Day feature
		item.GoalId
	);

	private static TrashedTaskItemDto MapTrashed(TaskItem item) => new(
		item.Id,
		item.Title,
		item.Description,
		item.IsCompleted,
		item.IsImportant,
		item.DueAt,
		item.CompletedAt,
		item.TaskListId,
		item.FocusAt,
		item.GoalId,
		item.TrashedAt ?? throw new InvalidOperationException("Only trashed tasks can be mapped as Trash results."));
}
