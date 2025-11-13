namespace OwnPlanner.Domain.Notes;

public interface INotesListRepository
{
	Task<NotesList?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NotesList>> ListAsync(bool includeArchived, CancellationToken ct = default);
	Task AddAsync(NotesList notesList, CancellationToken ct = default);
	Task UpdateAsync(NotesList notesList, CancellationToken ct = default);
	Task DeleteAsync(NotesList notesList, CancellationToken ct = default);
}
