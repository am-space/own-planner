using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

public class PlanningContextRepository(AppDbContext db) : IPlanningContextRepository
{
	private readonly AppDbContext _db = db;

	public async Task<PlanningContext?> GetAsync(Guid id, CancellationToken ct = default)
		=> await _db.PlanningContexts.FirstOrDefaultAsync(c => c.Id == id, ct);

	public async Task<IReadOnlyList<PlanningContext>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
	{
		var query = _db.PlanningContexts.AsQueryable();
		if (!includeArchived)
			query = query.Where(c => c.Status != ContextStatus.Archived);

		// SQLite cannot translate ORDER BY on DateTime; order in-memory instead
		var contexts = await query.ToListAsync(ct);
		return contexts
			.OrderByDescending(c => c.UpdatedAt)
			.ToList();
	}

	public async Task AddAsync(PlanningContext context, CancellationToken ct = default)
	{
		await _db.PlanningContexts.AddAsync(context, ct);
		await _db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(PlanningContext context, CancellationToken ct = default)
	{
		_db.PlanningContexts.Update(context);
		await _db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(PlanningContext context, CancellationToken ct = default)
	{
		_db.PlanningContexts.Remove(context);
		await _db.SaveChangesAsync(ct);
	}
}
