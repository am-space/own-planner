namespace OwnPlanner.Application.Tasks;

public record TaskListDto(
	Guid Id,
	string Title,
	string? Description,
	string? Color,
	bool IsArchived,
	Guid? ContextId,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
