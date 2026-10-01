using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public sealed class TaskGoalLinkService(ITaskItemRepository tasks) : ITaskGoalLinkService
{
	public async Task<TaskItemDto> LinkAsync(Guid taskId, Guid goalId, CancellationToken ct = default)
	{
		var result = await tasks.LinkToActiveGoalAsync(taskId, goalId, ct);
		return result.Status switch
		{
			TaskGoalLinkStatus.Linked or TaskGoalLinkStatus.AlreadyLinked => TaskItemService.Map(result.Task!),
			TaskGoalLinkStatus.TaskNotFound => throw new KeyNotFoundException("Task not found"),
			TaskGoalLinkStatus.GoalNotFound => throw new KeyNotFoundException("Goal not found"),
			TaskGoalLinkStatus.TaskListUnavailable => throw new InvalidOperationException("The task's list is unavailable or archived."),
			TaskGoalLinkStatus.GoalInactive => throw new InvalidOperationException("Only active goals can be linked through this operation."),
			TaskGoalLinkStatus.ConflictingLink => throw new InvalidOperationException("The task is already linked to another goal. Ask the user before replacing it."),
			_ => throw new InvalidOperationException("Unexpected goal-link result.")
		};
	}
}
