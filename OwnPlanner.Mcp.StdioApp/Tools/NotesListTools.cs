using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Notes.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class NotesListTools
{
	private readonly INotesListService _service;

	public NotesListTools(INotesListService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "noteslist_list_create"), Description("Create a notes list. Returns notes list information.")]
	public async Task<object> CreateNotesList(string title, string? description = null, string? color = null)
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

	[McpServerTool(Name = "noteslist_list_get", Idempotent = true, ReadOnly = true), Description("Get a notes list by id.")]
	public async Task<object> GetNotesList(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Notes list not found" };
		return dto;
	}

	[McpServerTool(Name = "noteslist_list_all", Idempotent = true, ReadOnly = true), Description("List all notes lists. Set includeArchived=true to include archived lists.")]
	public async Task<object> ListNotesLists(bool includeArchived = false)
	{
		var lists = await _service.ListAsync(includeArchived);
		return lists;
	}

	[McpServerTool(Name = "noteslist_list_update"), Description("Update a notes list's title, description, or color.")]
	public async Task<object> UpdateNotesList(Guid id, string? title = null, string? description = null, string? color = null)
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

	[McpServerTool(Name = "noteslist_list_archive"), Description("Archive a notes list by id.")]
	public async Task<object> ArchiveNotesList(Guid id)
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

	[McpServerTool(Name = "noteslist_list_unarchive"), Description("Unarchive a notes list by id.")]
	public async Task<object> UnarchiveNotesList(Guid id)
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

	[McpServerTool(Name = "noteslist_list_delete"), Description("Delete a notes list by id.")]
	public async Task<object> DeleteNotesList(Guid id)
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
