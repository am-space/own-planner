namespace OwnPlanner.Application.Tasks;

/// <summary>Applies an explicitly authorized goal link without editing other task fields.</summary>
public interface ITaskGoalLinkService
{
	/// <summary>Links an available task to an active goal in the current user's planner.</summary>
	/// <remarks>Rejects unavailable/archived tasks, inactive goals and conflicting existing links.
	/// Repeating the same link is idempotent. Natural-language confirmation is handled by the caller.</remarks>
	Task<TaskItemDto> LinkAsync(Guid taskId, Guid goalId, CancellationToken ct = default);
}
