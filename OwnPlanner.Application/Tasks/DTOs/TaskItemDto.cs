namespace OwnPlanner.Application.Tasks.DTOs;

public record TaskItemDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	bool IsImportant, // Added
	DateTime CreatedAt,
	DateTime UpdatedAt,
	DateTime? DueAt,
	DateTime? CompletedAt,
	Guid TaskListId
);
