using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteListRepository(AppDbContext db)
	: RepositoryBase<NoteList, AppDbContext>(db), INoteListRepository
{
	public async Task<IReadOnlyList<NoteList>> ListAsync(bool includeArchived, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		var query = Set.AsQueryable();
		if (!includeArchived)
			query = query.Where(nl => !nl.IsArchived);
		if (contextId.HasValue)
			query = query.Where(nl => nl.ContextId == contextId.Value);
		else if (excludeUnassigned)
			query = query.Where(nl => nl.ContextId != null);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct);
		return lists
			.OrderByDescending(nl => nl.UpdatedAt)
			.ToList();
	}

	public override async Task AddAsync(NoteList noteList, CancellationToken ct = default)
	{
		await Set.AddAsync(noteList, ct);
		try
		{
			await Db.SaveChangesAsync(ct);
		}
		catch (DbUpdateException)
		{
			var exists = await Set.AsNoTracking().AnyAsync(nl => nl.Id == noteList.Id, ct);
			if (!exists)
				throw;
			// Concurrent insert: another instance already created the same row; safe to ignore.
			Db.Entry(noteList).State = EntityState.Detached;
		}
	}
}
