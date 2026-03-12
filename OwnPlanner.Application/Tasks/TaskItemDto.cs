namespace OwnPlanner.Application.Tasks;

public record TaskItemDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	bool IsImportant,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	DateTime? DueAt,
	DateTime? CompletedAt,
	Guid TaskListId,
	DateTime? FocusAt // My Day feature
);
