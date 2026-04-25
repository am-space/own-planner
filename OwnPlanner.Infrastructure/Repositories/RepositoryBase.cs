using Microsoft.EntityFrameworkCore;

namespace OwnPlanner.Infrastructure.Repositories;

public abstract class RepositoryBase<TEntity, TContext>(TContext db)
    where TEntity : class
    where TContext : DbContext
{
    protected readonly TContext Db = db;

    protected DbSet<TEntity> Set => Db.Set<TEntity>();

    public async Task<TEntity?> GetAsync(Guid id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public virtual async Task AddAsync(TEntity entity, CancellationToken ct = default)
    {
        await Set.AddAsync(entity, ct);
        await Db.SaveChangesAsync(ct);
    }

    public virtual async Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        Set.Update(entity);
        await Db.SaveChangesAsync(ct);
    }

    public virtual async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        Set.Remove(entity);
        await Db.SaveChangesAsync(ct);
    }
}
