using OwnPlanner.Domain.Contexts;

namespace OwnPlanner.Application.Contexts;

public record PlanningContextDto(
	Guid Id,
	string Name,
	ContextType Type,
	string? Description,
	ContextStatus Status,
	string? Color,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
