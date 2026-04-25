using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class GoalRepository(AppDbContext db)
	: RepositoryBase<Goal, AppDbContext>(db), IGoalRepository
{
	public async Task<IReadOnlyList<Goal>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
	{
		var query = Set.AsQueryable();
		if (!includeInactive)
			query = query.Where(g => g.Status == GoalStatus.Active);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var goals = await query.ToListAsync(ct);
		return goals
			.OrderByDescending(g => g.UpdatedAt)
			.ToList();
	}
}
