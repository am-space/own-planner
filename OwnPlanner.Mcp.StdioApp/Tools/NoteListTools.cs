using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Notes;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class NoteListTools
{
	private readonly INoteListService _service;

	public NoteListTools(INoteListService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "notelist_create"), Description("Create a note list. Returns note list information.")]
	public async Task<object> CreateNoteList(Guid contextId, string title, string? description = null, string? color = null)
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

	[McpServerTool(Name = "notelist_get", Idempotent = true, ReadOnly = true), Description("Get a note list by id.")]
	public async Task<object> GetNoteList(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Note list not found" };
		return dto;
	}

	[McpServerTool(Name = "notelist_all", Idempotent = true, ReadOnly = true), Description("List all note lists. Optionally filter by contextId. Set includeArchived=true to include archived lists. Set includeUnassigned=true to also return legacy lists that have no context assigned.")]
	public async Task<object> ListNoteLists(bool includeArchived = false, Guid? contextId = null, bool includeUnassigned = false)
	{
		var lists = await _service.ListAsync(includeArchived, contextId, excludeUnassigned: !includeUnassigned);
		return lists;
	}

	[McpServerTool(Name = "notelist_update"), Description("Update a note list's title, description, or color.")]
	public async Task<object> UpdateNoteList(Guid id, string? title = null, string? description = null, string? color = null)
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

	[McpServerTool(Name = "notelist_archive"), Description("Archive a note list by id.")]
	public async Task<object> ArchiveNoteList(Guid id)
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

	[McpServerTool(Name = "notelist_unarchive"), Description("Unarchive a note list by id.")]
	public async Task<object> UnarchiveNoteList(Guid id)
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

	[McpServerTool(Name = "notelist_delete"), Description("Delete a note list by id.")]
	public async Task<object> DeleteNoteList(Guid id)
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
