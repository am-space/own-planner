namespace OwnPlanner.Domain.Notes;

public interface INoteItemRepository
{
	Task<NoteItem?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NoteItem>> ListAsync(CancellationToken ct = default);
	Task<IReadOnlyList<NoteItem>> ListByNotesListAsync(Guid notesListId, CancellationToken ct = default);
	Task AddAsync(NoteItem note, CancellationToken ct = default);
	Task UpdateAsync(NoteItem note, CancellationToken ct = default);
	Task DeleteAsync(NoteItem note, CancellationToken ct = default);
}
