namespace OwnPlanner.Domain.Tasks;

public enum TaskGoalLinkStatus
{
	Linked,
	AlreadyLinked,
	TaskNotFound,
	TaskListUnavailable,
	GoalNotFound,
	GoalInactive,
	ConflictingLink
}

/// <summary>Outcome and consistent task snapshot from an atomic goal-link operation.</summary>
/// <remarks>Task is populated only for Linked and AlreadyLinked.</remarks>
public sealed record TaskGoalLinkResult(TaskGoalLinkStatus Status, TaskItem? Task = null);
