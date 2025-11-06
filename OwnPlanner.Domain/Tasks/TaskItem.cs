namespace OwnPlanner.Domain.Tasks;

public class TaskItem
{
	public Guid Id { get; private set; }
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }
	public bool IsCompleted { get; private set; }
	public DateTime CreatedAt { get; private set; }
	public DateTime UpdatedAt { get; private set; }
	public DateTime? DueAt { get; private set; }
	public DateTime? CompletedAt { get; private set; }

	// EF Core constructor
	private TaskItem() { }

	public TaskItem(string title, string? description = null, DateTime? dueAt = null)
	{
		Id = Guid.NewGuid();
		SetTitle(title);
		SetDescription(description);
		SetDueAt(dueAt);
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

	public void SetDueAt(DateTime? dueAt)
	{
		DueAt = dueAt.HasValue ? DateTime.SpecifyKind(dueAt.Value, DateTimeKind.Utc) : null;
		Touch();
	}

	public void Complete()
	{
		if (!IsCompleted)
		{
			IsCompleted = true;
			CompletedAt = DateTime.UtcNow;
			Touch();
		}
	}

	public void Reopen()
	{
		if (IsCompleted)
		{
			IsCompleted = false;
			CompletedAt = null;
			Touch();
		}
	}

	private void Touch() => UpdatedAt = DateTime.UtcNow;
}
