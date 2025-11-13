using OwnPlanner.Application.Notes.DTOs;

namespace OwnPlanner.Application.Notes.Interfaces;

public interface INotesListService
{
	Task<NotesListDto> CreateAsync(string title, string? description = null, string? color = null, CancellationToken ct = default);
	Task<NotesListDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<NotesListDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
	Task<NotesListDto> UpdateAsync(Guid id, string? title = null, string? description = null, string? color = null, CancellationToken ct = default);
	Task ArchiveAsync(Guid id, CancellationToken ct = default);
	Task UnarchiveAsync(Guid id, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
