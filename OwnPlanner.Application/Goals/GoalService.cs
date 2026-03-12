using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Goals;

public class GoalService(IGoalRepository repository) : IGoalService
{
	private readonly IGoalRepository _repository = repository;

	public async Task<GoalDto> CreateAsync(string title, GoalHorizon horizon, string? description = null, string? targetPeriod = null, DateTime? targetDate = null, string? metric = null, CancellationToken ct = default)
	{
		var goal = new Goal(title, horizon, description, targetPeriod, targetDate, metric);
		await _repository.AddAsync(goal, ct);
		return Map(goal);
	}

	public async Task<GoalDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var goal = await _repository.GetAsync(id, ct);
		return goal is null ? null : Map(goal);
	}

	public async Task<IReadOnlyList<GoalDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
	{
		var goals = await _repository.ListAsync(includeInactive, ct);
		return goals.Select(Map).ToList();
	}

	public async Task<GoalDto> UpdateAsync(Guid id, string? title = null, string? description = null, GoalHorizon? horizon = null, string? targetPeriod = null, DateTime? targetDate = null, GoalStatus? status = null, string? metric = null, string? metricCurrent = null, CancellationToken ct = default)
	{
		var goal = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Goal {id} not found");

		if (title is not null)
			goal.SetTitle(title);
		if (description is not null)
			goal.SetDescription(description);
		if (horizon is not null || targetPeriod is not null || targetDate is not null)
			goal.SetHorizon(horizon ?? goal.Horizon, targetPeriod ?? goal.TargetPeriod, targetDate ?? goal.TargetDate);
		if (status is not null)
			goal.SetStatus(status.Value);
		if (metric is not null)
			goal.SetMetric(metric);
		if (metricCurrent is not null)
			goal.SetMetricCurrent(metricCurrent);

		await _repository.UpdateAsync(goal, ct);
		return Map(goal);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var goal = await _repository.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Goal {id} not found");
		await _repository.DeleteAsync(goal, ct);
	}

	private static GoalDto Map(Goal goal) => new(
		goal.Id,
		goal.Title,
		goal.Description,
		goal.Horizon,
		goal.TargetPeriod,
		goal.TargetDate,
		goal.Status,
		goal.Metric,
		goal.MetricCurrent,
		goal.CreatedAt,
		goal.UpdatedAt
	);
}
