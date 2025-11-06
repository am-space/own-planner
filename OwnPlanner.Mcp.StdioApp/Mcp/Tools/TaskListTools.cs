using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Mcp.Tools;

[McpServerToolType]
public class TaskListTools
{
	private readonly ITaskListService _service;

	public TaskListTools(ITaskListService service)
	{
		_service = service;
	}

	[McpServerTool, Description("Create a task. Returns task information.")]
	public async Task<object> CreateTask(string title, string? description = null)
	{
		var dto = await _service.CreateAsync(title, description);
		return dto;
	}

	[McpServerTool, Description("List tasks. Set includeCompleted=false to filter.")]
	public async Task<object> ListTasks(bool includeCompleted = true)
	{
		var list = await _service.ListAsync(includeCompleted);
		return list;
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
