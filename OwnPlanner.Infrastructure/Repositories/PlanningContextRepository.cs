using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class PlanningContextRepository(AppDbContext db)
	: RepositoryBase<PlanningContext, AppDbContext>(db), IPlanningContextRepository
{
	public async Task<IReadOnlyList<PlanningContext>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		var query = Set.AsQueryable();
		if (!includeArchived)
			query = query.Where(c => c.Status != ContextStatus.Archived);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var contexts = await query.ToListAsync(ct);
		return contexts
			.OrderByDescending(c => c.UpdatedAt)
			.ToList();
	}
}
