using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Contexts;
using OwnPlanner.Domain.Contexts;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public class PlanningContextTools(IPlanningContextService service)
{
	private readonly IPlanningContextService _service = service;

	[McpServerTool(Name = "context_create"), Description("Create a planning context. Type must be Area (ongoing area of life, e.g. Health) or Project (time-bounded, e.g. Q2 Launch). Returns the created context.")]
	public async Task<object> CreateContext(
		string name,
		string type,
		string? description = null,
		string? color = null)
	{
		if (!Enum.TryParse<ContextType>(type, ignoreCase: true, out var parsedType))
			return new { error = $"Invalid type '{type}'. Valid values: Area, Project." };

		try
		{
			var dto = await _service.CreateAsync(name, parsedType, description, color);
			return dto;
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "context_get", Idempotent = true, ReadOnly = true), Description("Get a planning context by id.")]
	public async Task<object> GetContext(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Planning context not found" };
		return dto;
	}

	[McpServerTool(Name = "context_list", Idempotent = true, ReadOnly = true), Description("List planning contexts. By default Archived contexts are excluded. Set includeArchived=true to include them. Active, Paused, and Completed contexts are always returned.")]
	public async Task<object> ListContexts(bool includeArchived = false)
	{
		var contexts = await _service.ListAsync(includeArchived);
		return contexts;
	}

	[McpServerTool(Name = "context_update"), Description("""
		Update a planning context. Only provided fields are changed.
		Valid type values: Area, Project.
		Valid status values: Active, Paused, Completed, Archived.
		""")]
	public async Task<object> UpdateContext(
		Guid id,
		string? name = null,
		string? type = null,
		string? description = null,
		string? status = null,
		string? color = null)
	{
		ContextType? parsedType = null;
		if (!string.IsNullOrEmpty(type))
		{
			if (!Enum.TryParse<ContextType>(type, ignoreCase: true, out var t))
				return new { error = $"Invalid type '{type}'. Valid values: Area, Project." };
			parsedType = t;
		}

		ContextStatus? parsedStatus = null;
		if (!string.IsNullOrEmpty(status))
		{
			if (!Enum.TryParse<ContextStatus>(status, ignoreCase: true, out var s))
				return new { error = $"Invalid status '{status}'. Valid values: Active, Paused, Completed, Archived." };
			parsedStatus = s;
		}

		try
		{
			var dto = await _service.UpdateAsync(id, name, parsedType, description, parsedStatus, color);
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

	[McpServerTool(Name = "context_delete"), Description("Permanently delete a planning context by id.")]
	public async Task<object> DeleteContext(Guid id)
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

