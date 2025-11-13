using OwnPlanner.Application.Notes.DTOs;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Application.Notes;

public class NoteListService(INoteListRepository repository) : INoteListService
{
	private readonly INoteListRepository _repository = repository;

	public async Task<NoteListDto> CreateAsync(string title, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var noteList = new NoteList(title, description, color);
		await _repository.AddAsync(noteList, ct);
		return Map(noteList);
	}

	public async Task<NoteListDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var noteList = await _repository.GetAsync(id, ct);
		return noteList is null ? null : Map(noteList);
	}

	public async Task<IReadOnlyList<NoteListDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		var noteLists = await _repository.ListAsync(includeArchived, ct);
		return noteLists.Select(Map).ToList();
	}

	public async Task<NoteListDto> UpdateAsync(Guid id, string? title = null, string? description = null, string? color = null, CancellationToken ct = default)
	{
		var noteList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NoteList {id} not found");
		
		if (title is not null)
			noteList.SetTitle(title);
		if (description is not null)
			noteList.SetDescription(description);
		if (color is not null)
			noteList.SetColor(color);

		await _repository.UpdateAsync(noteList, ct);
		return Map(noteList);
	}

	public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
	{
		var noteList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NoteList {id} not found");
		noteList.Archive();
		await _repository.UpdateAsync(noteList, ct);
	}

	public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
	{
		var noteList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NoteList {id} not found");
		noteList.Unarchive();
		await _repository.UpdateAsync(noteList, ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var noteList = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"NoteList {id} not found");
		await _repository.DeleteAsync(noteList, ct);
	}

	private static NoteListDto Map(NoteList noteList) => new(
		noteList.Id,
		noteList.Title,
		noteList.Description,
		noteList.Color,
		noteList.IsArchived,
		noteList.CreatedAt,
		noteList.UpdatedAt
	);
}
