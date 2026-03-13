namespace OwnPlanner.Application.Notes;

public record NoteListDto(
	Guid Id,
	string Title,
	string? Description,
	string? Color,
	bool IsArchived,
	Guid? ContextId,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
