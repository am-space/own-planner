using OwnPlanner.Application.Notes.DTOs;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Domain.Notes;

namespace OwnPlanner.Application.Notes;

public class NoteItemService(INoteItemRepository repository, INotesListRepository notesListRepository) : INoteItemService
{
	private readonly INoteItemRepository _repository = repository;
	private readonly INotesListRepository _notesListRepository = notesListRepository;

	public async Task<NoteItemDto> CreateAsync(string title, Guid notesListId, string? content = null, CancellationToken ct = default)
	{
		// Validate that the notes list exists
		var notesList = await _notesListRepository.GetAsync(notesListId, ct);
		if (notesList is null)
			throw new KeyNotFoundException($"NotesList {notesListId} not found");

		var item = new NoteItem(title, notesListId, content);
		await _repository.AddAsync(item, ct);
		return Map(item);
	}

	public async Task<NoteItemDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct);
		return item is null ? null : Map(item);
	}

	public async Task<IReadOnlyList<NoteItemDto>> ListAsync(CancellationToken ct = default)
	{
		var items = await _repository.ListAsync(ct);
		return items.Select(Map).ToList();
	}

	public async Task<IReadOnlyList<NoteItemDto>> ListByNotesListAsync(Guid notesListId, CancellationToken ct = default)
	{
		var items = await _repository.ListByNotesListAsync(notesListId, ct);
		return items.Select(Map).ToList();
	}

	public async Task<NoteItemDto> UpdateAsync(Guid id, string? title = null, string? content = null, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Note {id} not found");
		
		if (title is not null)
			item.SetTitle(title);
		if (content is not null)
			item.SetContent(content);

		await _repository.UpdateAsync(item, ct);
		return Map(item);
	}

	public async Task AssignToListAsync(Guid noteId, Guid notesListId, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(noteId, ct) ?? throw new KeyNotFoundException($"Note {noteId} not found");
		
		// Validate that the notes list exists
		var notesList = await _notesListRepository.GetAsync(notesListId, ct);
		if (notesList is null)
			throw new KeyNotFoundException($"NotesList {notesListId} not found");

		item.AssignToList(notesListId);
		await _repository.UpdateAsync(item, ct);
	}

	public async Task PinAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Note {id} not found");
		item.Pin();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task UnpinAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Note {id} not found");
		item.Unpin();
		await _repository.UpdateAsync(item, ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var item = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Note {id} not found");
		await _repository.DeleteAsync(item, ct);
	}

	private static NoteItemDto Map(NoteItem item) => new(
		item.Id,
		item.Title,
		item.Content,
		item.IsPinned,
		item.CreatedAt,
		item.UpdatedAt,
		item.NotesListId
	);
}
