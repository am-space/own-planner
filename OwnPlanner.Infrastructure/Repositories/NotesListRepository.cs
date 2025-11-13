using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NotesListRepository(AppDbContext db) : INotesListRepository
{
	private readonly AppDbContext _db = db;

	public async Task<NotesList?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.NotesLists.FirstOrDefaultAsync(nl => nl.Id == id, ct);

	public async Task<IReadOnlyList<NotesList>> ListAsync(bool includeArchived, CancellationToken ct = default)
	{
		var query = _db.NotesLists.AsQueryable();
		if (!includeArchived)
			query = query.Where(nl => !nl.IsArchived);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct);
		return lists
			.OrderByDescending(nl => nl.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(NotesList notesList, CancellationToken ct = default)
	{
		await _db.NotesLists.AddAsync(notesList, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(NotesList notesList, CancellationToken ct = default)
	{
		_db.NotesLists.Update(notesList);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(NotesList notesList, CancellationToken ct = default)
	{
		_db.NotesLists.Remove(notesList);
		await _db.SaveChangesAsync(ct);
	}
}
