namespace OwnPlanner.Application.Tasks;

/// <summary>Task data retained in Trash together with its original restore destination.</summary>
public sealed record TrashedTaskItemDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	bool IsImportant,
	DateTime? DueAt,
	DateTime? CompletedAt,
	Guid TaskListId,
	DateTime? FocusAt,
	Guid? GoalId,
	DateTime TrashedAt);
