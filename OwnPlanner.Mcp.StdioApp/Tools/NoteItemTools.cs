using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Notes.Interfaces;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class NoteItemTools
{
	private readonly INoteItemService _service;

	public NoteItemTools(INoteItemService service)
	{
		_service = service;
	}

	[McpServerTool(Name = "noteitem_item_create"), Description("Create a note. NotesListId is required. Returns note information.")]
	public async Task<object> CreateNote(string title, Guid notesListId, string? content = null)
	{
		try
		{
			var dto = await _service.CreateAsync(title, notesListId, content);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_item_get", Idempotent = true, ReadOnly = true), Description("Get a note by id.")]
	public async Task<object> GetNote(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Note not found" };
		return dto;
	}

	[McpServerTool(Name = "noteitem_item_list_all", Idempotent = true, ReadOnly = true), Description("List all notes ordered by pinned status and update time.")]
	public async Task<object> ListNotes()
	{
		var list = await _service.ListAsync();
		return list;
	}

	[McpServerTool(Name = "noteitem_list_items", Idempotent = true, ReadOnly = true), Description("List notes by notes list id.")]
	public async Task<object> ListNotesByList(Guid notesListId)
	{
		var list = await _service.ListByNotesListAsync(notesListId);
		return list;
	}

	[McpServerTool(Name = "noteitem_item_update"), Description("Update a note. Provide id and the fields to update (title or content).")]
	public async Task<object> UpdateNote(Guid id, string? title = null, string? content = null)
	{
		try
		{
			var dto = await _service.UpdateAsync(id, title, content);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_item_assign"), Description("Assign a note to a different notes list.")]
	public async Task<object> AssignNoteToList(Guid noteId, Guid notesListId)
	{
		try
		{
			await _service.AssignToListAsync(noteId, notesListId);
			return new { success = true, noteId, notesListId };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_item_pin"), Description("Pin a note by id.")]
	public async Task<object> PinNote(Guid id)
	{
		try
		{
			await _service.PinAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_item_unpin"), Description("Unpin a note by id.")]
	public async Task<object> UnpinNote(Guid id)
	{
		try
		{
			await _service.UnpinAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_item_delete"), Description("Delete a note by id.")]
	public async Task<object> DeleteNote(Guid id)
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
