namespace OwnPlanner.Domain.Notes;

public class NoteList : EntityBase
{
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }
	public string? Color { get; private set; }
	public bool IsArchived { get; private set; }

	// System lists are built-in lists that cannot be archived or deleted (e.g. "Inbox"). They are created via code, not user actions.
	public bool IsSystem { get; private set; }

	/// <summary>Optional reference to the owning <see cref="OwnPlanner.Domain.Contexts.PlanningContext"/>. Null for legacy/unassigned lists.</summary>
	public Guid? ContextId { get; private set; }

	// EF Core constructor
	private NoteList() { }

	private NoteList(Guid id, string title) : base(id)
	{
		SetTitle(title);
		IsSystem = true;
	}

	public static NoteList CreateSystem(Guid id, string title) => new(id, title);

	public NoteList(string title, string? description = null, string? color = null, Guid? contextId = null)
		: base(Guid.NewGuid())
	{
		SetTitle(title);
		SetDescription(description);
		SetColor(color);
		ContextId = contextId;
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

	public void SetContextId(Guid? contextId)
	{
		ContextId = contextId;
		Touch();
	}


}
