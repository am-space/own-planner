using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Tasks;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public class TaskItemTools
{
	/// <summary>
	/// Maximum number of original <see cref="TaskItemDto.Description"/> characters kept by list tools
	/// before truncation. Full description is available via <c>taskitem_get</c>. Keeps list payloads
	/// small so they don't dominate the model's context.
	/// </summary>
	private const int DescriptionPreviewMaxLength = 200;
	private const string TruncationSuffix = "… [truncated — call taskitem_get for full description]";

	private readonly ITaskItemService _service;

	public TaskItemTools(ITaskItemService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "taskitem_create"), Description("Create a task. TaskListId is required. dueAt is optional and should be used only for real deadlines (external or fixed commitments). Returns task information.")]
	public async Task<object> CreateTask(string title, Guid taskListId, string? description = null, string? dueAt = null, Guid? goalId = null)
	{
		try
		{
			DateTime? dueDate = null;
			if (!string.IsNullOrEmpty(dueAt) && DateTime.TryParse(dueAt, out var parsed))
				dueDate = parsed;

			var dto = await _service.CreateAsync(title, taskListId, description, dueDate, goalId: goalId);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_get", Idempotent = true, ReadOnly = true), Description("Get a task by id.")]
	public async Task<object> GetTask(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Task not found" };
		return dto;
	}

	[McpServerTool(Name = "taskitem_list_items", Idempotent = true, ReadOnly = true), Description("List tasks (paginated, default 25 per page, max 100). If taskListId is provided, lists tasks by that list; otherwise lists all tasks. Ordered by planned focus date (soonest first, unscheduled last), then most recently updated. Returns { items, totalCount, offset, limit, hasMore }; increase offset to page when hasMore is true. Each item's description is a short preview — call taskitem_get for the full description. Set includeCompleted=true to also include completed tasks.")]
	public async Task<object> ListTasks(Guid? taskListId = null, bool onlyImportant = false, bool includeCompleted = false, int limit = 25, int offset = 0)
	{
		var page = taskListId.HasValue
			? await _service.ListByTaskListPagedAsync(taskListId.Value, includeCompleted, onlyImportant, offset, limit)
			: await _service.ListPagedAsync(includeCompleted, onlyImportant, offset, limit);

		return ToEnvelope(page);
	}

	[McpServerTool(Name = "taskitem_update"), Description("Update a task. Provide id and the fields to update (title, description, dueAt, or goalId). dueAt is for real deadlines only. Set clearGoalId=true to remove the goal association.")]
	public async Task<object> UpdateTask(Guid id, string? title = null, string? description = null, string? dueAt = null, Guid? goalId = null, bool clearGoalId = false)
	{
		try
		{
			DateTime? dueDate = null;
			if (!string.IsNullOrEmpty(dueAt))
			{
				if (DateTime.TryParse(dueAt, out var parsed))
					dueDate = parsed;
				else
					return new { error = "Invalid date format for dueAt" };
			}

			var dto = await _service.UpdateAsync(id, title, description, dueDate, goalId: goalId, clearGoalId: clearGoalId);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_list_by_goal", Idempotent = true, ReadOnly = true), Description("List tasks linked to a specific goal (paginated, default 25 per page, max 100). Ordered by planned focus date (soonest first, unscheduled last), then most recently updated. Returns { items, totalCount, offset, limit, hasMore }; increase offset to page when hasMore is true. Each item's description is a short preview — call taskitem_get for the full description. Set includeCompleted=true to also return completed tasks.")]
	public async Task<object> ListTasksByGoal(Guid goalId, bool includeCompleted = false, int limit = 25, int offset = 0)
	{
		var page = await _service.ListByGoalPagedAsync(goalId, includeCompleted, offset, limit);
		return ToEnvelope(page);
	}

	[McpServerTool(Name = "taskitem_assign"), Description("Assign a task to a different list.")]
	public async Task<object> AssignTaskToList(Guid taskId, Guid taskListId)
	{
		try
		{
			await _service.AssignToListAsync(taskId, taskListId);
			return new { success = true, taskId, taskListId };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_complete"), Description("Complete a task by id.")]
	public async Task<object> CompleteTask(Guid id, CancellationToken cancellationToken = default)
	{
		try
		{
			await _service.CompleteAsync(id, cancellationToken);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_reopen"), Description("Reopen a completed task by id.")]
	public async Task<object> ReopenTask(Guid id)
	{
		try
		{
			await _service.ReopenAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_delete"), Description("Move an active task to Trash by id. This is recoverable and does not permanently delete task data.")]
	public async Task<object> DeleteTask(Guid id, CancellationToken cancellationToken = default)
	{
		try
		{
			await _service.DeleteAsync(id, cancellationToken);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_list_trash", Idempotent = true, ReadOnly = true), Description("List tasks in Trash (paginated, default 25 per page, max 100). Returns { items, totalCount, offset, limit, hasMore }. Trashed tasks are excluded from normal task queries.")]
	public async Task<object> ListTrash(int limit = 25, int offset = 0, CancellationToken cancellationToken = default)
	{
		var page = await _service.ListTrashedPagedAsync(offset, limit, cancellationToken);
		return new
		{
			items = page.Items.Select(item => item with { Description = TruncateDescription(item.Description) }),
			page.TotalCount,
			page.Offset,
			page.Limit,
			page.HasMore
		};
	}

	[McpServerTool(Name = "taskitem_restore"), Description("Restore a task from Trash to its original task list. Fails safely if that list no longer exists.")]
	public async Task<object> RestoreTask(Guid id, CancellationToken cancellationToken = default)
	{
		try
		{
			await _service.RestoreAsync(id, cancellationToken);
			return new { success = true, id };
		}
		catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_set_important"), Description("Set or unset the important flag for a task.")]
	public async Task<object> SetTaskImportant(Guid id, bool isImportant)
	{
		try
		{
			var dto = await _service.UpdateAsync(id, isImportant: isImportant);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "taskitem_list_by_focus_date", Idempotent = true, ReadOnly = true), Description("List tasks by focus date (My Day / planned work date), paginated (default 25 per page, max 100). focusDate represents when you plan to work on the task, not the task deadline. If focusDate is empty, uses current UTC date. Returns { items, totalCount, offset, limit, hasMore }; increase offset to page when hasMore is true. Each item's description is a short preview — call taskitem_get for the full description. Set includeCompleted=true to also include completed tasks.")]
	public async Task<object> ListTasksByFocusDate(string? focusDate = null, bool includeCompleted = false, int limit = 25, int offset = 0)
	{
		DateTime date;
		if (string.IsNullOrWhiteSpace(focusDate))
		{
			date = DateTime.UtcNow.Date;
		}
		else if (!DateTime.TryParse(focusDate, out date))
		{
			return new { error = "Invalid date format for focusDate" };
		}
		var page = await _service.ListByFocusDatePagedAsync(date, includeCompleted, offset, limit);
		return ToEnvelope(page);
	}

	[McpServerTool(Name = "taskitem_set_focus_date"), Description("Set or clear the focus date (My Day / planned work date) for a task. Use this for weekly planning to decide when to work on a task. This is separate from dueAt, which is for real deadlines. Provide id and focusDate. If focusDate is empty, clears the focus date.")]
	public async Task<object> SetTaskFocusDate(Guid id, string? focusDate = null)
	{
		if (string.IsNullOrWhiteSpace(focusDate))
		{
			try
			{
				await _service.ClearFocusDateAsync(id);
				return new { success = true, id, focusDate = (string?)null };
			}
			catch (KeyNotFoundException ex)
			{
				return new { error = ex.Message };
			}
		}
		if (!DateTime.TryParse(focusDate, out var date))
			return new { error = "Invalid date format for focusDate" };
		try
		{
			await _service.SetFocusDateAsync(id, date);
			return new { success = true, id, focusDate = date };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	/// <summary>
	/// Wraps a page in the tool envelope, projecting each task to the slim <see cref="TaskItemListDto"/>
	/// (no audit timestamps) with a truncated description preview. The shape tells the model the page
	/// bounds and whether to keep paging.
	/// </summary>
	private static object ToEnvelope(PagedResult<TaskItemDto> page) => new
	{
		items = page.Items.Select(ToListItem).ToList(),
		totalCount = page.TotalCount,
		offset = page.Offset,
		limit = page.Limit,
		hasMore = page.HasMore
	};

	private static TaskItemListDto ToListItem(TaskItemDto dto) => new(
		dto.Id,
		dto.Title,
		TruncateDescription(dto.Description),
		dto.IsCompleted,
		dto.IsImportant,
		dto.DueAt,
		dto.CompletedAt,
		dto.TaskListId,
		dto.FocusAt,
		dto.GoalId);

	private static string? TruncateDescription(string? description)
		=> string.IsNullOrEmpty(description) || description.Length <= DescriptionPreviewMaxLength
			? description
			: description[..DescriptionPreviewMaxLength] + TruncationSuffix;
}
