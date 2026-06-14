namespace OwnPlanner.Domain;

/// <summary>
/// Base class for all domain entities. Provides identity and audit timestamps.
/// </summary>
public abstract class EntityBase
{
	public Guid Id { get; private set; }
	public DateTime CreatedAt { get; private set; }
	public DateTime UpdatedAt { get; private set; }

	// EF Core constructor
	protected EntityBase() { }

	protected EntityBase(Guid id)
	{
		Id = id;
		var now = MonotonicClock.UtcNow();
		CreatedAt = now;
		UpdatedAt = now;
	}

	protected void Touch() => UpdatedAt = MonotonicClock.UtcNow();
}
