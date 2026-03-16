using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class GoalRepository(AppDbContext db) : IGoalRepository
{
	private readonly AppDbContext _db = db;

	public async Task<Goal?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.Goals.FirstOrDefaultAsync(g => g.Id == id, ct);

	public async Task<IReadOnlyList<Goal>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
	{
		var query = _db.Goals.AsQueryable();
		if (!includeInactive)
			query = query.Where(g => g.Status == GoalStatus.Active);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var goals = await query.ToListAsync(ct);
		return goals
			.OrderByDescending(g => g.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(Goal goal, CancellationToken ct = default)
	{
		await _db.Goals.AddAsync(goal, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(Goal goal, CancellationToken ct = default)
	{
		_db.Goals.Update(goal);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(Goal goal, CancellationToken ct = default)
	{
		_db.Goals.Remove(goal);
		await _db.SaveChangesAsync(ct);
	}
}
