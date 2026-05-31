using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class GoalRepository(IPlannerDbContextFactory dbContextFactory)
	: PlannerRepositoryBase<Goal>(dbContextFactory), IGoalRepository
{
	public async Task<IReadOnlyList<Goal>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		var query = db.Goals.AsQueryable();
		if (!includeInactive)
			query = query.Where(g => g.Status == GoalStatus.Active);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var goals = await query.ToListAsync(ct).ConfigureAwait(false);
		return goals
			.OrderByDescending(g => g.UpdatedAt)
			.ToList();
	}
}
