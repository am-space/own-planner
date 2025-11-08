namespace OwnPlanner.Application.Tasks.DTOs;

public record TaskListDto(
	Guid Id,
	string Title,
	string? Description,
	string? Color,
	bool IsArchived,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
