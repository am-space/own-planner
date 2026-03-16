namespace OwnPlanner.Domain.Contexts;

/// <summary>
/// A named area of life or a time-bounded project that organises related work.
/// Contexts are always flat — there is no parent/child hierarchy between them.
/// Each context owns a set of <c>TaskList</c>s and <c>NoteList</c>s,
/// and optionally links its tasks to top-level <see cref="OwnPlanner.Domain.Goals.Goal"/>s.
/// </summary>
public class PlanningContext : EntityBase
{
	public string Name { get; private set; } = string.Empty;

	/// <summary>
	/// Classifies the context as an ongoing <see cref="ContextType.Area"/> (e.g. "Health", "Finance")
	/// or a time-bounded <see cref="ContextType.Project"/> that has a defined end.
	/// </summary>
	public ContextType Type { get; private set; }

	public string? Description { get; private set; }

	/// <summary>
	/// Lifecycle status. Only <see cref="ContextStatus.Archived"/> contexts are excluded from default listings;
	/// <see cref="ContextStatus.Paused"/> and <see cref="ContextStatus.Completed"/> remain visible.
	/// </summary>
	public ContextStatus Status { get; private set; }

	/// <summary>Optional UI color hint (e.g. a hex code or named color token).</summary>
	public string? Color { get; private set; }

	// EF Core constructor
	private PlanningContext() { }

	public PlanningContext(string name, ContextType type, string? description = null, string? color = null)
		: base(Guid.NewGuid())
	{
		SetName(name);
		SetDescription(description);
		SetColor(color);
		Type = type;
		Status = ContextStatus.Active;
	}

	public void SetName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Name is required", nameof(name));
		Name = name.Trim();
		Touch();
	}

	public void SetDescription(string? description)
	{
		Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		Touch();
	}

	public void SetType(ContextType type)
	{
		Type = type;
		Touch();
	}

	public void SetStatus(ContextStatus status)
	{
		Status = status;
		Touch();
	}

	public void SetColor(string? color)
	{
		Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
		Touch();
	}


}
