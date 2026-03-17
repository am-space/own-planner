namespace OwnPlanner.Domain.Notes;

public class NoteItem : EntityBase
{
	public string Title { get; private set; } = string.Empty;
	public string? Content { get; private set; }
	public bool IsPinned { get; private set; }
	public Guid NoteListId { get; private set; }
	/// <summary>Optional soft reference to a Goal. No FK constraint — stale references are acceptable.</summary>
	public Guid? GoalId { get; private set; }

	// EF Core constructor
	private NoteItem() { }

	public NoteItem(string title, Guid noteListId, string? content = null, Guid? goalId = null)
		: base(Guid.NewGuid())
	{
		SetTitle(title);
		NoteListId = noteListId;
		SetContent(content);
		GoalId = goalId;
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

	public void SetGoalId(Guid? goalId)
	{
		GoalId = goalId;
		Touch();
	}

	public void AssignToList(Guid noteListId)
	{
		NoteListId = noteListId;
		Touch();
	}


}
