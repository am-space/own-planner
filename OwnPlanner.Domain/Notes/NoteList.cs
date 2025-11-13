namespace OwnPlanner.Domain.Notes;

public class NoteList
{
	public Guid Id { get; private set; }
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }
	public string? Color { get; private set; }
	public bool IsArchived { get; private set; }
	public DateTime CreatedAt { get; private set; }
	public DateTime UpdatedAt { get; private set; }

	// EF Core constructor
	private NoteList() { }

	public NoteList(string title, string? description = null, string? color = null)
	{
		Id = Guid.NewGuid();
		SetTitle(title);
		SetDescription(description);
		SetColor(color);
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

	public void SetDescription(string? description)
	{
		Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		Touch();
	}

	public void SetColor(string? color)
	{
		Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
		Touch();
	}

	public void Archive()
	{
		if (!IsArchived)
		{
			IsArchived = true;
			Touch();
		}
	}

	public void Unarchive()
	{
		if (IsArchived)
		{
			IsArchived = false;
			Touch();
		}
	}

	private void Touch() => UpdatedAt = DateTime.UtcNow;
}
