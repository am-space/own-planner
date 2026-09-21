namespace OwnPlanner.Domain.WeeklyReviews;

/// <summary>Local calendar preferences in one user's planner database. Enabling requires an explicit timezone.</summary>
public sealed class WeeklyReviewPreferences
{
	public int Id { get; set; } = 1;
	public bool Enabled { get; set; }
	public string? TimeZoneId { get; set; }
	public int WeekStart { get; set; } = 1;
	public string ReminderTime { get; set; } = "18:00";
	public string Channel { get; set; } = "telegram";
}

/// <summary>A shared review with frozen calendar boundaries and a separately claimed delivery occurrence.</summary>
public sealed class WeeklyReviewState
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public DateOnly TargetWeek { get; set; }
	public string TimeZoneId { get; set; } = "UTC";
	public DateTime StartsAtUtc { get; set; }
	public DateTime EndsAtUtc { get; set; }
	public DateTime ScheduledAtUtc { get; set; }
	public string Status { get; set; } = "notStarted";
	public DateTime? DeferredUntilUtc { get; set; }
	public int Occurrence { get; set; }
	public string Delivery { get; set; } = "pending";
	public int Attempts { get; set; }
	public DateTime? RetryAtUtc { get; set; }
	public bool OfferedInChat { get; set; }
}
