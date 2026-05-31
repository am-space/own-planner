using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

/// <summary>
/// Base repository for planner entities backed by per-user <see cref="AppDbContext"/> instances.
/// A fresh context is created per repository operation so planner data access can be delayed until
/// an authenticated or tool-scoped user context is actually required.
/// </summary>
public abstract class PlannerRepositoryBase<TEntity>(IPlannerDbContextFactory dbContextFactory)
	where TEntity : EntityBase
{
	private readonly IPlannerDbContextFactory _dbContextFactory = dbContextFactory;

	protected ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
		=> _dbContextFactory.CreateAsync(cancellationToken);

	public async Task<TEntity?> GetAsync(Guid id, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		return await db.Set<TEntity>().FindAsync([id], ct).ConfigureAwait(false);
	}

	public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		await db.Set<TEntity>().AddAsync(entity, ct).ConfigureAwait(false);
		await db.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public virtual async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		db.Set<TEntity>().Update(entity);
		await db.SaveChangesAsync(ct).ConfigureAwait(false);
	}

	public virtual async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
	{
		await using var db = await CreateDbContextAsync(ct).ConfigureAwait(false);
		db.Set<TEntity>().Remove(entity);
		await db.SaveChangesAsync(ct).ConfigureAwait(false);
	}
}

