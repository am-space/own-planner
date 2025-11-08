using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Mcp.Tools;

[McpServerToolType]
public class TaskItemTools
{
	private readonly ITaskItemService _service;

	public TaskItemTools(ITaskItemService service)
	{
		_service = service;
	}

	[McpServerTool, Description("Create a task. Returns task information.")]
	public async Task<object> CreateTask(string title, string? description = null, string? dueAt = null)
	{
		DateTime? dueDate = null;
		if (!string.IsNullOrEmpty(dueAt) && DateTime.TryParse(dueAt, out var parsed))
			dueDate = parsed;
		
		var dto = await _service.CreateAsync(title, description, dueDate);
		return dto;
	}

	[McpServerTool, Description("Get a task by id.")]
	public async Task<object> GetTask(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Task not found" };
		return dto;
	}

	[McpServerTool, Description("List tasks. Set includeCompleted=false to filter out completed tasks.")]
	public async Task<object> ListTasks(bool includeCompleted = true)
	{
		var list = await _service.ListAsync(includeCompleted);
		return list;
	}

	[McpServerTool, Description("List tasks by task list id. Use null for tasks without a list.")]
	public async Task<object> ListTasksByList(string? taskListId = null, bool includeCompleted = true)
	{
		Guid? listId = null;
		if (!string.IsNullOrEmpty(taskListId) && Guid.TryParse(taskListId, out var parsed))
			listId = parsed;
		
		var list = await _service.ListByTaskListAsync(listId, includeCompleted);
		return list;
	}

	[McpServerTool, Description("Assign a task to a list. Use null for taskListId to unassign.")]
	public async Task<object> AssignTaskToList(Guid taskId, string? taskListId = null)
	{
		Guid? listId = null;
		if (!string.IsNullOrEmpty(taskListId) && Guid.TryParse(taskListId, out var parsed))
			listId = parsed;
		
		await _service.AssignToListAsync(taskId, listId);
		return new { success = true, taskId, taskListId = listId };
	}

	[McpServerTool, Description("Complete a task by id.")]
	public async Task<object> CompleteTask(Guid id)
	{
		await _service.CompleteAsync(id);
		return new { success = true, id };
	}

	[McpServerTool, Description("Reopen a completed task by id.")]
	public async Task<object> ReopenTask(Guid id)
	{
		await _service.ReopenAsync(id);
		return new { success = true, id };
	}

	[McpServerTool, Description("Delete a task by id.")]
	public async Task<object> DeleteTask(Guid id)
	{
		await _service.DeleteAsync(id);
		return new { success = true, id };
	}
}
