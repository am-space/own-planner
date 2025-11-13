using OwnPlanner.Application.Notes.DTOs;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Application.Notes;

public class NotesListService(INotesListRepository repository) : INotesListService
{
	private readonly INotesListRepository _repository = repository;

	public async Task<NotesListDto> CreateAsync(string title, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var notesList = new NotesList(title, description, color);
		await _repository.AddAsync(notesList, ct);
		return Map(notesList);
	}

	public async Task<NotesListDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var notesList = await _repository.GetAsync(id, ct);
		return notesList is null ? null : Map(notesList);
	}

	public async Task<IReadOnlyList<NotesListDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		var notesLists = await _repository.ListAsync(includeArchived, ct);
		return notesLists.Select(Map).ToList();
	}

	public async Task<NotesListDto> UpdateAsync(Guid id, string? title = null, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var notesList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NotesList {id} not found");
		
		if (title is not null)
			notesList.SetTitle(title);
		if (description is not null)
			notesList.SetDescription(description);
		if (color is not null)
			notesList.SetColor(color);

		await _repository.UpdateAsync(notesList, ct);
		return Map(notesList);
	}

	public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
	{
		var notesList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NotesList {id} not found");
		notesList.Archive();
		await _repository.UpdateAsync(notesList, ct);
	}

	public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
	{
		var notesList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NotesList {id} not found");
		notesList.Unarchive();
		await _repository.UpdateAsync(notesList, ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var notesList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NotesList {id} not found");
		await _repository.DeleteAsync(notesList, ct);
	}

	private static NotesListDto Map(NotesList notesList) => new(
		notesList.Id,
		notesList.Title,
		notesList.Description,
		notesList.Color,
		notesList.IsArchived,
		notesList.CreatedAt,
		notesList.UpdatedAt
	);
}
