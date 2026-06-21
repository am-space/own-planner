namespace OwnPlanner.Application.Tasks;

/// <summary>
/// Slim task shape returned by the list tools. Compared with <see cref="TaskItemDto"/> it drops the
/// audit timestamps (<c>CreatedAt</c>/<c>UpdatedAt</c>) the model never reasons over and carries only
/// a truncated <see cref="Description"/> preview. This keeps list payloads small regardless of which
/// serializer a caller uses; full details remain available via <c>taskitem_get</c>.
/// </summary>
public sealed record TaskItemListDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	bool IsImportant,
	DateTime? DueAt,
	DateTime? CompletedAt,
	Guid TaskListId,
	DateTime? FocusAt,
	Guid? GoalId
);
