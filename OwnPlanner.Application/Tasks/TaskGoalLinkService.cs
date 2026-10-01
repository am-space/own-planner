using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks;

public sealed class TaskGoalLinkService(
	ITaskItemService tasks, IGoalRepository goals, ITaskListRepository lists) : ITaskGoalLinkService
{
	public async Task<TaskItemDto> LinkAsync(Guid taskId, Guid goalId, CancellationToken ct = default)
	{
		var task = await tasks.GetAsync(taskId, ct)
			?? throw new KeyNotFoundException("Task not found");
		var list = await lists.GetAsync(task.TaskListId, ct);
		if (list is null || list.IsArchived)
			throw new InvalidOperationException("The task's list is unavailable or archived.");
		var goal = await goals.GetAsync(goalId, ct)
			?? throw new KeyNotFoundException("Goal not found");
		if (goal.Status != GoalStatus.Active)
			throw new InvalidOperationException("Only active goals can be linked through this operation.");
		if (task.GoalId == goalId)
			return task;
		if (task.GoalId.HasValue)
			throw new InvalidOperationException("The task is already linked to another goal. Ask the user before replacing it.");
		return await tasks.UpdateAsync(taskId, goalId: goalId, ct: ct);
	}
}
