using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Goals;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class GoalTools(IGoalService service)
{
	private readonly IGoalService _service = service;

	[McpServerTool(Name = "goal_create"), Description("""
		Create a new goal. Horizon controls the time granularity:
		  - Monthly / Quarterly / Yearly: provide targetPeriod (e.g. "2025-06", "2025-Q2", "2025"); omit targetDate.
		  - TargetDate: provide targetDate (ISO 8601, e.g. "2025-12-31"); omit targetPeriod.
		Returns the created goal.
		""")]
	public async Task<object> CreateGoal(
		string title,
		string horizon,
		string? description = null,
		string? targetPeriod = null,
		string? targetDate = null,
		string? metric = null)
	{
		if (!Enum.TryParse<GoalHorizon>(horizon, ignoreCase: true, out var parsedHorizon))
			return new { error = $"Invalid horizon '{horizon}'. Valid values: Monthly, Quarterly, Yearly, TargetDate." };

		DateTime? parsedTargetDate = null;
		if (!string.IsNullOrEmpty(targetDate))
		{
			if (!DateTime.TryParse(targetDate, out var dt))
				return new { error = $"Invalid targetDate format '{targetDate}'. Use ISO 8601, e.g. \"2025-12-31\"." };
			parsedTargetDate = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
		}

		try
		{
			var dto = await _service.CreateAsync(title, parsedHorizon, description, targetPeriod, parsedTargetDate, metric);
			return dto;
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "goal_get", Idempotent = true, ReadOnly = true), Description("Get a goal by id.")]
	public async Task<object> GetGoal(Guid id)
	{
		var dto = await _service.GetAsync(id);
		if (dto is null)
			return new { error = "Goal not found" };
		return dto;
	}

	[McpServerTool(Name = "goal_list", Idempotent = true, ReadOnly = true), Description("List goals. By default only Active goals are returned. Set includeInactive=true to also include Achieved and Dropped goals.")]
	public async Task<object> ListGoals(bool includeInactive = false)
	{
		var goals = await _service.ListAsync(includeInactive);
		return goals;
	}

	[McpServerTool(Name = "goal_update"), Description("""
		Update a goal. Only provided fields are changed.
		Horizon fields (horizon, targetPeriod, targetDate) are updated together — omitted ones keep their current value,
		except when switching to TargetDate horizon which always clears targetPeriod.
		Valid horizon values: Monthly, Quarterly, Yearly, TargetDate.
		Valid status values: Active, Achieved, Dropped.
		""")]
	public async Task<object> UpdateGoal(
		Guid id,
		string? title = null,
		string? description = null,
		string? horizon = null,
		string? targetPeriod = null,
		string? targetDate = null,
		string? status = null,
		string? metric = null,
		string? metricCurrent = null)
	{
		GoalHorizon? parsedHorizon = null;
		if (!string.IsNullOrEmpty(horizon))
		{
			if (!Enum.TryParse<GoalHorizon>(horizon, ignoreCase: true, out var h))
				return new { error = $"Invalid horizon '{horizon}'. Valid values: Monthly, Quarterly, Yearly, TargetDate." };
			parsedHorizon = h;
		}

		DateTime? parsedTargetDate = null;
		if (!string.IsNullOrEmpty(targetDate))
		{
			if (!DateTime.TryParse(targetDate, out var dt))
				return new { error = $"Invalid targetDate format '{targetDate}'. Use ISO 8601, e.g. \"2025-12-31\"." };
			parsedTargetDate = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
		}

		GoalStatus? parsedStatus = null;
		if (!string.IsNullOrEmpty(status))
		{
			if (!Enum.TryParse<GoalStatus>(status, ignoreCase: true, out var s))
				return new { error = $"Invalid status '{status}'. Valid values: Active, Achieved, Dropped." };
			parsedStatus = s;
		}

		try
		{
			var dto = await _service.UpdateAsync(id, title, description, parsedHorizon, targetPeriod, parsedTargetDate, parsedStatus, metric, metricCurrent);
			return dto;
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	[McpServerTool(Name = "goal_delete"), Description("Permanently delete a goal by id.")]
	public async Task<object> DeleteGoal(Guid id)
	{
		try
		{
			await _service.DeleteAsync(id);
			return new { success = true, id };
		}
		catch (KeyNotFoundException ex)
		{
			return new { error = ex.Message };
		}
	}
}
