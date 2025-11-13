using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteListRepository(AppDbContext db) : INoteListRepository
{
	private readonly AppDbContext _db = db;

	public async Task<NoteList?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.NoteLists.FirstOrDefaultAsync(nl => nl.Id == id, ct);

	public async Task<IReadOnlyList<NoteList>> ListAsync(bool includeArchived, CancellationToken ct = default)
	{
		var query = _db.NoteLists.AsQueryable();
		if (!includeArchived)
			query = query.Where(nl => !nl.IsArchived);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct);
		return lists
			.OrderByDescending(nl => nl.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(NoteList noteList, CancellationToken ct = default)
	{
		await _db.NoteLists.AddAsync(noteList, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(NoteList noteList, CancellationToken ct = default)
	{
		_db.NoteLists.Update(noteList);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(NoteList noteList, CancellationToken ct = default)
	{
		_db.NoteLists.Remove(noteList);
		await _db.SaveChangesAsync(ct);
	}
}
