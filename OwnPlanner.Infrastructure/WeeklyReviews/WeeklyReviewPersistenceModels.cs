using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

/// <summary>Singleton storage row; its integer key is a persistence detail, not a Domain entity identity.</summary>
public sealed class WeeklyReviewPreferencesRow
{
	public int Id { get; set; } = 1;
	public bool Enabled { get; set; }
	public string? TimeZoneId { get; set; }
	public int WeekStart { get; set; } = 1;
	public string ReminderTime { get; set; } = "18:00";
	public string Channel { get; set; } = "telegram";

	public WeeklyReviewPreferences ToSnapshot() => new()
	{
		Id = Id, Enabled = Enabled, TimeZoneId = TimeZoneId, WeekStart = WeekStart,
		ReminderTime = ReminderTime, Channel = Channel
	};

	public void Apply(WeeklyReviewPreferences snapshot)
	{
		Enabled = snapshot.Enabled;
		TimeZoneId = snapshot.TimeZoneId;
		WeekStart = snapshot.WeekStart;
		ReminderTime = snapshot.ReminderTime;
		Channel = snapshot.Channel;
	}
}

/// <summary>Storage row for Application-owned workflow state; deliberately not a planner Domain entity.</summary>
public sealed class WeeklyReviewRow
{
	public Guid Id { get; set; }
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

	public WeeklyReviewState ToSnapshot() => new()
	{
		Id = Id, TargetWeek = TargetWeek, TimeZoneId = TimeZoneId,
		StartsAtUtc = StartsAtUtc, EndsAtUtc = EndsAtUtc, ScheduledAtUtc = ScheduledAtUtc,
		Status = Status, DeferredUntilUtc = DeferredUntilUtc, Occurrence = Occurrence,
		Delivery = Delivery, Attempts = Attempts, RetryAtUtc = RetryAtUtc, OfferedInChat = OfferedInChat
	};

	public void Apply(WeeklyReviewState snapshot)
	{
		TargetWeek = snapshot.TargetWeek;
		TimeZoneId = snapshot.TimeZoneId;
		StartsAtUtc = snapshot.StartsAtUtc;
		EndsAtUtc = snapshot.EndsAtUtc;
		ScheduledAtUtc = snapshot.ScheduledAtUtc;
		Status = snapshot.Status;
		DeferredUntilUtc = snapshot.DeferredUntilUtc;
		Occurrence = snapshot.Occurrence;
		Delivery = snapshot.Delivery;
		Attempts = snapshot.Attempts;
		RetryAtUtc = snapshot.RetryAtUtc;
		OfferedInChat = snapshot.OfferedInChat;
	}
}
