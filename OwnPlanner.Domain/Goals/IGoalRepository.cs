namespace OwnPlanner.Domain.Goals;

public interface IGoalRepository
{
	Task<Goal?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<Goal>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
	Task AddAsync(Goal goal, CancellationToken ct = default);
	Task UpdateAsync(Goal goal, CancellationToken ct = default);
	Task DeleteAsync(Goal goal, CancellationToken ct = default);
}
