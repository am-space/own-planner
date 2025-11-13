using OwnPlanner.Application.Notes.DTOs;

namespace OwnPlanner.Application.Notes.Interfaces;

public interface INoteItemService
{
	Task<NoteItemDto> CreateAsync(string title, Guid notesListId, string? content = null, CancellationToken ct = default);
	Task<NoteItemDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NoteItemDto>> ListAsync(CancellationToken ct = default);
	Task<IReadOnlyList<NoteItemDto>> ListByNotesListAsync(Guid notesListId, CancellationToken ct = default);
	Task<NoteItemDto> UpdateAsync(Guid id, string? title = null, string? content = null, CancellationToken ct = default);
	Task AssignToListAsync(Guid noteId, Guid notesListId, CancellationToken ct = default);
	Task PinAsync(Guid id, CancellationToken ct = default);
	Task UnpinAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
