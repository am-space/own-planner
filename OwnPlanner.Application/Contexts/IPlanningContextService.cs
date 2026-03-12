using OwnPlanner.Domain.Contexts;

namespace OwnPlanner.Application.Contexts;

/// <summary>
/// Application service for managing <see cref="OwnPlanner.Domain.Contexts.PlanningContext"/> entities.
/// </summary>
public interface IPlanningContextService
{
	/// <summary>Creates a new context of the given <paramref name="type"/> (Area or Project).</summary>
	Task<PlanningContextDto> CreateAsync(string name, ContextType type, string? description = null, string? color = null, CancellationToken ct = default);

	/// <summary>Returns the context with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
	Task<PlanningContextDto?> GetAsync(Guid id, CancellationToken ct = default);

	/// <summary>
	/// Returns all contexts. By default contexts with <see cref="ContextStatus.Archived"/> status are excluded;
	/// pass <paramref name="includeArchived"/> = <c>true</c> to include them.
	/// Contexts with <see cref="ContextStatus.Paused"/> or <see cref="ContextStatus.Completed"/> status
	/// are always included.
	/// </summary>
	Task<IReadOnlyList<PlanningContextDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default);

	/// <summary>
	/// Updates the specified fields of a context. Only non-<c>null</c> arguments are applied.
	/// <para>
	/// All lifecycle transitions (Active, Paused, Completed, Archived) are handled through
	/// the <paramref name="status"/> parameter.
	/// </para>
	/// </summary>
	Task<PlanningContextDto> UpdateAsync(Guid id, string? name = null, ContextType? type = null, string? description = null, ContextStatus? status = null, string? color = null, CancellationToken ct = default);

	/// <summary>Permanently deletes the context with the given <paramref name="id"/>.</summary>
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
