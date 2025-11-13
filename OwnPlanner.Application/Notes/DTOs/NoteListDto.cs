namespace OwnPlanner.Application.Notes.DTOs;

public record NoteListDto(
	Guid Id,
	string Title,
	string? Description,
	string? Color,
	bool IsArchived,
	DateTime CreatedAt,
	DateTime UpdatedAt
);
