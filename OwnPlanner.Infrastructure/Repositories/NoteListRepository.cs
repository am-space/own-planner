using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class NoteListRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<NoteList>(dbContextFactory), INoteListRepository
{
	public async Task<IReadOnlyList<NoteList>> ListAsync(bool includeArchived, Guid? contextId = null, bool excludeUnassigned = false, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.NoteLists.AsQueryable();
		if (!includeArchived)
			query = query.Where(nl => !nl.IsArchived);
		if (contextId.HasValue)
			query = query.Where(nl => nl.ContextId == contextId.Value);
		else if (excludeUnassigned)
			query = query.Where(nl => nl.ContextId != null);

		// SQLite cannot translate ORDER BY on DateTimeOffset; order in-memory instead
		var lists = await query.ToListAsync(ct).ConfigureAwait(false);
		return lists
			.OrderByDescending(nl => nl.UpdatedAt)
			.ToList();
	}

	public override async Task AddAsync(NoteList noteList, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var set = db.NoteLists;
		await set.AddAsync(noteList, ct).ConfigureAwait(false);
		try
		{
			await db.SaveChangesAsync(ct).ConfigureAwait(false);
		}
		catch (DbUpdateException)
		{
			var exists = await set.AsNoTracking().AnyAsync(nl => nl.Id == noteList.Id, ct).ConfigureAwait(false);
			if (!exists)
				throw;
			// Concurrent insert: another instance already created the same row; safe to ignore.
			db.Entry(noteList).State = EntityState.Detached;
		}
	}
}
