using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteItemRepository(AppDbContext db) : INoteItemRepository
{
	private readonly AppDbContext _db = db;

	public async Task<NoteItem?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.NoteItems.FirstOrDefaultAsync(n => n.Id == id, ct);

	public async Task<IReadOnlyList<NoteItem>> ListAsync(CancellationToken ct = default)
	{
		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var items = await _db.NoteItems.ToListAsync(ct);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task<IReadOnlyList<NoteItem>> ListByNotesListAsync(Guid notesListId, CancellationToken ct = default)
	{
		var query = _db.NoteItems.Where(n => n.NotesListId == notesListId);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var items = await query.ToListAsync(ct);
		return items
			.OrderByDescending(n => n.IsPinned)
			.ThenByDescending(n => n.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(NoteItem note, CancellationToken ct = default)
	{
		await _db.NoteItems.AddAsync(note, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(NoteItem note, CancellationToken ct = default)
	{
		_db.NoteItems.Update(note);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(NoteItem note, CancellationToken ct = default)
	{
		_db.NoteItems.Remove(note);
		await _db.SaveChangesAsync(ct);
	}
}
