using OwnPlanner.Domain;

namespace OwnPlanner.Domain.Notes;

public class NoteList : EntityBase
{
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }
	public string? Color { get; private set; }
	public bool IsArchived { get; private set; }

	// EF Core constructor
	private NoteList() { }

	public NoteList(string title, string? description = null, string? color = null)
		: base(Guid.NewGuid())
	{
		SetTitle(title);
		SetDescription(description);
		SetColor(color);
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


}
