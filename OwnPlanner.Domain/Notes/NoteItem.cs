namespace OwnPlanner.Domain.Notes;

public class NoteItem
{
	public Guid Id { get; private set; }
	public string Title { get; private set; } = string.Empty;
	public string? Content { get; private set; }
	public bool IsPinned { get; private set; }
	public DateTime CreatedAt { get; private set; }
	public DateTime UpdatedAt { get; private set; }
	public Guid NotesListId { get; private set; }

	// EF Core constructor
	private NoteItem() { }

	public NoteItem(string title, Guid notesListId, string? content = null)
	{
		Id = Guid.NewGuid();
		SetTitle(title);
		NotesListId = notesListId;
		SetContent(content);
		var now = DateTime.UtcNow;
		CreatedAt = now;
		UpdatedAt = now;
	}

	public void SetTitle(string title)
	{
		if (string.IsNullOrWhiteSpace(title))
			throw new ArgumentException("Title is required", nameof(title));
		Title = title.Trim();
		Touch();
	}

	public void SetContent(string? content)
	{
		Content = string.IsNullOrWhiteSpace(content) ? null : content.Trim();
		Touch();
	}

	public void Pin()
	{
		if (!IsPinned)
		{
			IsPinned = true;
			Touch();
		}
	}

	public void Unpin()
	{
		if (IsPinned)
		{
			IsPinned = false;
			Touch();
		}
	}

	public void AssignToList(Guid notesListId)
	{
		NotesListId = notesListId;
		Touch();
	}

	private void Touch() => UpdatedAt = DateTime.UtcNow;
}
