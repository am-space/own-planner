using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class PlanningContextRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<PlanningContext>(dbContextFactory), IPlanningContextRepository
{
	public async Task<IReadOnlyList<PlanningContext>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.PlanningContexts.AsQueryable();
		if (!includeArchived)
			query = query.Where(c => c.Status != ContextStatus.Archived);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var contexts = await query.ToListAsync(ct).ConfigureAwait(false);
		return contexts
			.OrderByDescending(c => c.UpdatedAt)
			.ToList();
	}
}
