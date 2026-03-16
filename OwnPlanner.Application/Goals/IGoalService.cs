using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Goals;

/// <summary>
/// Application service for managing <see cref="OwnPlanner.Domain.Goals.Goal"/> entities.
/// </summary>
public interface IGoalService
{
	/// <summary>Creates a new goal with the specified horizon and optional planning fields.</summary>
	Task<GoalDto> CreateAsync(string title, GoalHorizon horizon, string? description = null, string? targetPeriod = null, DateTime? targetDate = null, string? metric = null, CancellationToken ct = default);

	/// <summary>Returns the goal with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
	Task<GoalDto?> GetAsync(Guid id, CancellationToken ct = default);

	/// <summary>
	/// Returns all goals. By default only <see cref="GoalStatus.Active"/> goals are returned;
	/// pass <paramref name="includeInactive"/> = <c>true</c> to also include
	/// <see cref="GoalStatus.Achieved"/> and <see cref="GoalStatus.Dropped"/> goals.
	/// </summary>
	Task<IReadOnlyList<GoalDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);

	/// <summary>
	/// Updates the specified fields of a goal. Only non-<c>null</c> arguments are applied.
	/// <para>
	/// <b>Horizon fields:</b> <paramref name="horizon"/>, <paramref name="targetPeriod"/>, and
	/// <paramref name="targetDate"/> are always updated together — if any of the three is provided,
	/// <see cref="OwnPlanner.Domain.Goals.Goal.SetHorizon"/> is called with the current values
	/// used as defaults for the omitted ones.
	/// </para>
	/// </summary>
	Task<GoalDto> UpdateAsync(Guid id, string? title = null, string? description = null, GoalHorizon? horizon = null, string? targetPeriod = null, DateTime? targetDate = null, GoalStatus? status = null, string? metric = null, string? metricCurrent = null, CancellationToken ct = default);

	/// <summary>Permanently deletes the goal with the given <paramref name="id"/>.</summary>
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
