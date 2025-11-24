using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class TaskListTools
{
	private readonly ITaskListService _service;

	public TaskListTools(ITaskListService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "tasklist_create"), Description("Create a task list. Returns task list information.")]
	public async Task<object> CreateTaskList(string title, string? description = null, string? color = null)
	{
		try
		{
			var dto = await _service.CreateAsync(title, description, color);
			return dto;
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "tasklist_get", Idempotent = true, ReadOnly = true), Description("Get a task list by id.")]
	public async Task<object> GetTaskList(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Task list not found" };
		return dto;
	}

	[McpServerTool(Name = "tasklist_all", Idempotent = true, ReadOnly = true), Description("List all task lists. Set includeArchived=true to include archived lists.")]
	public async Task<object> ListTaskLists(bool includeArchived = false)
	{
		var lists = await _service.ListAsync(includeArchived);
		return lists;
	}

	[McpServerTool(Name = "tasklist_update"), Description("Update a task list's title, description, or color.")]
	public async Task<object> UpdateTaskList(Guid id, string? title = null, string? description = null, string? color = null)
	{
		try
		{
			var dto = await _service.UpdateAsync(id, title, description, color);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "tasklist_archive"), Description("Archive a task list by id.")]
	public async Task<object> ArchiveTaskList(Guid id)
	{
		try
		{
			await _service.ArchiveAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "tasklist_unarchive"), Description("Unarchive a task list by id.")]
	public async Task<object> UnarchiveTaskList(Guid id)
	{
		try
		{
			await _service.UnarchiveAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "tasklist_delete"), Description("Delete a task list by id. Tasks in the list will be orphaned (moved to no list).")]
	public async Task<object> DeleteTaskList(Guid id)
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
}
