namespace OwnPlanner.Application.Notes.DTOs;

public record NoteItemDto(
	Guid Id,
	string Title,
	string? Content,
	bool IsPinned,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	Guid NoteListId
);
