namespace OwnPlanner.Domain.Notes;

public interface INoteListRepository
{
	Task<NoteList?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NoteList>> ListAsync(bool includeArchived, CancellationToken ct = default);
	Task AddAsync(NoteList noteList, CancellationToken ct = default);
	Task UpdateAsync(NoteList noteList, CancellationToken ct = default);
	Task DeleteAsync(NoteList noteList, CancellationToken ct = default);
}
