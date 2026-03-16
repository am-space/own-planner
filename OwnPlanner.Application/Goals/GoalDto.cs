using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Goals;

public record GoalDto(
	Guid Id,
	string Title,
	string? Description,
	GoalHorizon Horizon,
	string? TargetPeriod,
	DateTime? TargetDate,
	GoalStatus Status,
	string? Metric,
	string? MetricCurrent,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
