namespace OwnPlanner.Application.Tasks.DTOs;

public record TaskItemDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	DateTime? DueAt,
	DateTime? CompletedAt,
	Guid? TaskListId
);
