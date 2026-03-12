namespace OwnPlanner.Domain.Contexts;

public interface IPlanningContextRepository
{
	Task<PlanningContext?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IReadOnlyList<PlanningContext>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
	Task AddAsync(PlanningContext context, CancellationToken ct = default);
	Task UpdateAsync(PlanningContext context, CancellationToken ct = default);
	Task DeleteAsync(PlanningContext context, CancellationToken ct = default);
}
