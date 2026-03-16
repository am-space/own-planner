namespace OwnPlanner.Application.Notes;

public interface INoteListService
{
	Task<NoteListDto> CreateAsync(string title, Guid contextId, string? description = null, string? color = null, CancellationToken ct = default);
	Task<NoteListDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NoteListDto>> ListAsync(bool includeArchived = false, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default);
	Task<NoteListDto> UpdateAsync(Guid id, string? title = null, string? description = null, string? color = null, CancellationToken ct = default);
	Task ArchiveAsync(Guid id, CancellationToken ct = default);
	Task UnarchiveAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
