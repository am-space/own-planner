namespace OwnPlanner.Application.Notes;

public interface INoteListService
{
	Task<NoteListDto> CreateAsync(string title, Guid contextId, string? description = null, string? color = null, CancellationToken ct = default);
	Task<NoteListDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NoteListDto>> ListAsync(bool includeArchived = false, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default);
	/// <summary>
	/// Updates the specified note list. All parameters are optional; omitting a parameter leaves the existing value unchanged.
	/// </summary>
	/// <remarks>
	/// <paramref name="contextId"/> is opt-in: passing <see langword="null"/> leaves the current context assignment unchanged.
	/// The context cannot be cleared to <see langword="null"/> via this method.
	/// </remarks>
	Task<NoteListDto> UpdateAsync(Guid id, string? title = null, Guid? contextId = null, string? description = null, string? color = null, CancellationToken ct = default);
	Task ArchiveAsync(Guid id, CancellationToken ct = default);
	Task UnarchiveAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
