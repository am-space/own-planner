using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public class TaskListTools
{
	private readonly ITaskListService _service;

	public TaskListTools(ITaskListService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "tasklist_create"), Description("Create a task list. Returns task list information.")]
	public async Task<object> CreateTaskList(Guid contextId, string title, string? description = null, string? color = null)
	{
		try
		{
			var dto = await _service.CreateAsync(title, contextId, description, color);
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

	[McpServerTool(Name = "tasklist_all", Idempotent = true, ReadOnly = true), Description("List all task lists. Optionally filter by contextId. Set includeArchived=true to include archived lists. Set includeUnassigned=true to also return legacy lists that have no context assigned.")]
	public async Task<object> ListTaskLists(bool includeArchived = false, Guid? contextId = null, bool includeUnassigned = false)
	{
		var lists = await _service.ListAsync(includeArchived, contextId, excludeUnassigned: !includeUnassigned);
		return lists;
	}

	[McpServerTool(Name = "tasklist_update"), Description("Update a task list's title, contextId, description, or color. All parameters are optional; omitting one leaves the existing value unchanged. contextId is opt-in: omitting it or passing null leaves the current context assignment unchanged.")]
	public async Task<object> UpdateTaskList(Guid id, string? title = null, Guid? contextId = null, string? description = null, string? color = null)
	{
		try
		{
			var dto = await _service.UpdateAsync(id, title, contextId, description, color);
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
		catch (InvalidOperationException ex)
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
		catch (InvalidOperationException ex)
		{
			return new { error = ex.Message };
		}
	}
}

