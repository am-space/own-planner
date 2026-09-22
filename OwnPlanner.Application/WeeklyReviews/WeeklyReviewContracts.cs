namespace OwnPlanner.Application.WeeklyReviews;

public sealed record WeeklyReviewTask(Guid Id, string Title, Guid TaskListId, DateTime? FocusAt, DateTime? DueAt,
	DateTime Revision, bool Carryover, bool Overdue, bool DueInTargetWeek);
public sealed record WeeklyReviewReport(DateTime AsOfUtc, int CarryoverCount, int OverdueCount, int DueInTargetWeekCount,
	int TotalCount, int Offset, int Limit, IReadOnlyList<WeeklyReviewTask> Tasks);
public sealed record WeeklyReviewView(WeeklyReviewState Review, WeeklyReviewReport Report);
public sealed record WeeklyReminderClaim(Guid ReviewId, int Occurrence, string Text);

/// <summary>Serializes per-user review transitions in a database transaction; callbacks never perform network I/O.</summary>
public interface IWeeklyReviewStore
{
	/// <summary>Reads a detached snapshot or unconfigured defaults without creating or updating any rows.</summary>
	Task<WeeklyReviewPreferences> GetPreferencesAsync(CancellationToken ct = default);
	/// <summary>Runs a short state transition atomically, persisting changed preferences and review rows.</summary>
	Task<T> UpdateAsync<T>(Func<WeeklyReviewPreferences, IList<WeeklyReviewState>, T> transition, CancellationToken ct = default);
	/// <summary>Returns exact counts and a bounded, stable page of active incomplete tasks with selection reasons.</summary>
	Task<WeeklyReviewReport> ReportAsync(WeeklyReviewState review, DateTime nowUtc, int offset, int limit, CancellationToken ct = default);
}

/// <summary>Current user's weekly preferences, calendar review and separately tracked notification lifecycle.</summary>
public interface IWeeklyReviewService
{
	/// <summary>Reads preferences without implicitly opting in or selecting a timezone.</summary>
	Task<WeeklyReviewPreferences> GetPreferencesAsync(CancellationToken ct = default);
	/// <summary>Validates and persists explicit preferences. Disabling suppresses automatic offers and delivery.</summary>
	Task<WeeklyReviewPreferences> ConfigureAsync(bool enabled, string? timeZoneId, int weekStart, string reminderTime, string channel = "telegram", CancellationToken ct = default);
	/// <summary>Reads fresh bounded review data. Opening changes only review state, never tasks. An explicit targetWeek selects an existing review by its frozen calendar date.</summary>
	Task<WeeklyReviewView> OpenAsync(Guid? reviewId = null, int offset = 0, int limit = 20, CancellationToken ct = default, DateOnly? targetWeek = null);
	/// <summary>Completes, skips or defers the specified review; deferral is a local time in its frozen timezone.</summary>
	Task<WeeklyReviewState> TransitionAsync(Guid reviewId, string action, string? localTime = null, CancellationToken ct = default);
	/// <summary>Claims a single contextual offer for an eligible missed review after a suitable planning interaction.</summary>
	Task<string?> OfferAsync(CancellationToken ct = default);
	/// <summary>Claims eligible nonempty delivery before sending, excluding expired or already claimed occurrences.</summary>
	Task<WeeklyReminderClaim?> ClaimReminderAsync(CancellationToken ct = default);
	/// <summary>Records delivery or a known rejection; ambiguous outcomes remain claimed and are never retried automatically.</summary>
	Task FinishDeliveryAsync(WeeklyReminderClaim claim, bool delivered, bool retryableRejection = false, CancellationToken ct = default);
}
