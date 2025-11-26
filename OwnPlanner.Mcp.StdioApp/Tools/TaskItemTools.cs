using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class TaskItemTools
{
	private readonly ITaskItemService _service;

	public TaskItemTools(ITaskItemService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "taskitem_create"), Description("Create a task. TaskListId is required. Returns task information.")]
	public async Task<object> CreateTask(string title, Guid taskListId, string? description = null, string? dueAt = null)
	{
		try
		{
			DateTime? dueDate = null;
			if (!string.IsNullOrEmpty(dueAt) && DateTime.TryParse(dueAt, out var parsed))
				dueDate = parsed;
			
			var dto = await _service.CreateAsync(title, taskListId, description, dueDate);
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

	[McpServerTool(Name = "taskitem_list_items", Idempotent = true, ReadOnly = true), Description("List tasks. If taskListId is provided, lists tasks by task list id; otherwise, lists all tasks. Set includeCompleted=true to get also completed tasks.")]
	public async Task<object> ListTasks(Guid? taskListId = null, bool onlyImportant = false, bool includeCompleted = false)
	{
		IEnumerable<OwnPlanner.Application.Tasks.DTOs.TaskItemDto> list;
		if (taskListId.HasValue)
		{
			list = await _service.ListByTaskListAsync(taskListId.Value, includeCompleted);
		}
		else
		{
			list = await _service.ListAsync(includeCompleted);
		}

		if (onlyImportant)
		{
			list = list.Where(x => x.IsImportant);
		}
		return list.ToList();
	}

	[McpServerTool(Name = "taskitem_update"), Description("Update a task. Provide id and the fields to update (title, description, or dueAt).")]
	public async Task<object> UpdateTask(Guid id, string? title = null, string? description = null, string? dueAt = null)
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
			
			var dto = await _service.UpdateAsync(id, title, description, dueDate);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
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
	public async Task<object> CompleteTask(Guid id)
	{
		try
		{
			await _service.CompleteAsync(id);
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

	[McpServerTool(Name = "taskitem_delete"), Description("Delete a task by id.")]
	public async Task<object> DeleteTask(Guid id)
	{
		try
		{
			await _service.DeleteAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
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

	[McpServerTool(Name = "taskitem_list_by_focus_date", Idempotent = true, ReadOnly = true), Description("List tasks by focus date (My Day). Provide focusDate. Set includeCompleted=true to get also completed tasks.")]
	public async Task<object> ListTasksByFocusDate(string focusDate, bool includeCompleted = false)
	{
		if (!DateTime.TryParse(focusDate, out var date))
			return new { error = "Invalid date format for focusDate" };
		var list = await _service.ListByFocusDateAsync(date, includeCompleted);
		return list.ToList();
	}

	[McpServerTool(Name = "taskitem_set_focus_date"), Description("Set or clear the focus date (My Day) for a task. Provide id and focusDate. If focusDate is empty, clears the focus date.")]
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
}
