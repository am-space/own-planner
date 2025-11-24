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

	[McpServerTool(Name = "noteitem_create"), Description("Create a note. NoteListId is required. Returns note information.")]
	public async Task<object> CreateNote(string title, Guid noteListId, string? content = null)
	{
		try
		{
			var dto = await _service.CreateAsync(title, noteListId, content);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_get", Idempotent = true, ReadOnly = true), Description("Get a note by id.")]
	public async Task<object> GetNote(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Note not found" };
		return dto;
	}

	[McpServerTool(Name = "noteitem_list_items", Idempotent = true, ReadOnly = true), Description("List notes. If noteListId is provided, lists notes by note list id; otherwise, lists all notes ordered by pinned status and update time.")]
	public async Task<object> ListNotes(Guid? noteListId = null)
	{
		if (noteListId.HasValue)
		{
			var list = await _service.ListByNoteListAsync(noteListId.Value);
			return list;
		}
		else
		{
			var list = await _service.ListAsync();
			return list;
		}
	}

	[McpServerTool(Name = "noteitem_update"), Description("Update a note. Provide id and the fields to update (title or content).")]
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

	[McpServerTool(Name = "noteitem_assign"), Description("Assign a note to a different note list.")]
	public async Task<object> AssignNoteToList(Guid noteId, Guid noteListId)
	{
		try
		{
			await _service.AssignToListAsync(noteId, noteListId);
			return new { success = true, noteId, noteListId };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "noteitem_pin"), Description("Pin a note by id.")]
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

	[McpServerTool(Name = "noteitem_unpin"), Description("Unpin a note by id.")]
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

	[McpServerTool(Name = "noteitem_delete"), Description("Delete a note by id.")]
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
