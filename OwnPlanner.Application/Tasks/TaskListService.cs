using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public class TaskListService(ITaskListRepository repository) : ITaskListService
{
	private readonly ITaskListRepository _repository = repository;

	public async Task<TaskListDto> CreateAsync(string title, Guid contextId, string? description = null, string? color = null, CancellationToken ct = default)
	{
		if (contextId == Guid.Empty)
			throw new ArgumentException("A valid context ID is required to create a task list.", nameof(contextId));
		var taskList = new TaskList(title, description, color, contextId);
		await _repository.AddAsync(taskList, ct);
		return Map(taskList);
	}

	public async Task<TaskListDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var taskList = await _repository.GetAsync(id, ct);
		return taskList is null ? null : Map(taskList);
	}

	public async Task<IReadOnlyList<TaskListDto>> ListAsync(bool includeArchived = false, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		var taskLists = await _repository.ListAsync(includeArchived, contextId, excludeUnassigned, ct);
		return taskLists.Select(Map).ToList();
	}

	public async Task<TaskListDto> UpdateAsync(Guid id, string? title = null, Guid? contextId = null, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var taskList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"TaskList {id} not found");
		
		if (title is not null)
			taskList.SetTitle(title);
		// Keep context updates opt-in so omitted arguments leave existing assignments unchanged.
		if (contextId.HasValue)
			taskList.SetContextId(contextId.Value);
		if (description is not null)
			taskList.SetDescription(description);
		if (color is not null)
			taskList.SetColor(color);

		await _repository.UpdateAsync(taskList, ct);
		return Map(taskList);
	}

	public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
	{
		var taskList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"TaskList {id} not found");
		if (taskList.IsSystem)
			throw new InvalidOperationException("System lists cannot be archived.");
		taskList.Archive();
		await _repository.UpdateAsync(taskList, ct);
	}

	public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
	{
		var taskList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"TaskList {id} not found");
		taskList.Unarchive();
		await _repository.UpdateAsync(taskList, ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var taskList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"TaskList {id} not found");
		if (taskList.IsSystem)
			throw new InvalidOperationException("System lists cannot be deleted.");
		await _repository.DeleteAsync(taskList, ct);
	}

	private static TaskListDto Map(TaskList taskList) => new(
		taskList.Id,
		taskList.Title,
		taskList.Description,
		taskList.Color,
		taskList.IsArchived,
		taskList.IsSystem,
		taskList.ContextId,
		taskList.CreatedAt,
		taskList.UpdatedAt
	);
}
