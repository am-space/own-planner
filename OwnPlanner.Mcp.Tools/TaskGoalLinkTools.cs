using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class TaskGoalLinkTools(ITaskGoalLinkService service)
{
	[McpServerTool(Name = "taskitem_link_goal"), Description("Link a task to an active goal only after the user confirms the suggested link. Changes only goalId; preserves other task fields. Rejects missing tasks/goals, archived lists, inactive goals and conflicting links. Repeating the same link is safe. Never use a relevance match as authorization.")]
	public async Task<object> LinkGoal(Guid taskId, Guid goalId, CancellationToken cancellationToken = default)
	{
		try
		{
			return await service.LinkAsync(taskId, goalId, cancellationToken);
		}
		catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
		{
			return new { error = ex.Message };
		}
	}
}
