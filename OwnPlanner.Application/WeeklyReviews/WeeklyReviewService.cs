using System.Globalization;
using OwnPlanner.Application.Goals;

namespace OwnPlanner.Application.WeeklyReviews;

public sealed class WeeklyReviewService(IWeeklyReviewStore store, TimeProvider clock) : IWeeklyReviewService
{
	private DateTime Now => clock.GetUtcNow().UtcDateTime;

	public Task<WeeklyReviewPreferences> GetPreferencesAsync(CancellationToken ct = default) =>
		store.GetPreferencesAsync(ct);

	public Task<WeeklyReviewPreferences> ConfigureAsync(bool enabled, string? timeZoneId, int weekStart, string reminderTime,
		string channel = "telegram", CancellationToken ct = default)
	{
		WeeklyReviewCalendar.Validate(timeZoneId, weekStart, reminderTime, channel, enabled);
		return store.UpdateAsync((preferences, _) =>
		{
			preferences.Enabled = enabled;
			preferences.TimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId;
			preferences.WeekStart = weekStart;
			preferences.ReminderTime = reminderTime;
			preferences.Channel = channel;
			return preferences;
		}, ct);
	}

	public async Task<WeeklyReviewView> OpenAsync(Guid? reviewId = null, int offset = 0, int limit = 20, CancellationToken ct = default, DateOnly? targetWeek = null, bool conversational = false, bool present = true)
	{
		if (offset < 0 || limit is < 1 or > 50) throw new ArgumentException("Use offset >= 0 and limit between 1 and 50.");
		var now = Now;
		var review = await store.UpdateAsync((preferences, reviews) =>
		{
			if (reviewId.HasValue && targetWeek.HasValue) throw new ArgumentException("Specify reviewId or targetWeek, not both.");
			var review = reviewId.HasValue ? Find(reviews, reviewId.Value) : targetWeek.HasValue
				? reviews.FirstOrDefault(r => r.TargetWeek == targetWeek) ?? throw new KeyNotFoundException("Weekly review not found.")
				: DueDeferral(reviews, now) ?? Resolve(preferences, reviews, now, false);
			if (present && review.Status == "notStarted") review.Status = "inProgress";
			return review;
		}, ct);
		var report = await BuildReportAsync(review, now, offset, limit, ct);
		var suggestCreatingGoals = present && conversational && report.Goals.ActiveCount == 0 &&
			await store.UpdateAsync((_, reviews) =>
			{
				var current = Find(reviews, review.Id);
				if (current.GoalCreationOfferedInChat || Terminal(current)) return false;
				current.GoalCreationOfferedInChat = true;
				return true;
			}, ct);
		string? pausedMention = null;
		string? activeWarning = null;
		if (present && offset == 0 && !Terminal(review) && now < review.EndsAtUtc)
		{
			var paused = await store.PausedGoalsAsync(ct);
			var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(review.TimeZoneId)));
			var month = new DateOnly(localDate.Year, localDate.Month, 1);
			var warning = GoalFocusGuidance.Warning(report.Goals.ActiveCount);
			var claims = await store.UpdateAsync((preferences, reviews) =>
			{
				var current = Find(reviews, review.Id);
				if (Terminal(current) || now >= current.EndsAtUtc) return (Paused: false, Limit: false);
				var mention = paused.Count > 0 && (preferences.PausedGoalsMentionMonth is null || preferences.PausedGoalsMentionMonth < month);
				if (mention) preferences.PausedGoalsMentionMonth = month;
				var limitWarning = warning is not null && !current.ActiveGoalLimitWarned;
				if (limitWarning) current.ActiveGoalLimitWarned = true;
				return (Paused: mention, Limit: limitWarning);
			}, ct);
			if (claims.Paused) pausedMention = PausedMention(paused, now);
			if (claims.Limit) activeWarning = warning;
		}
		return new(review, report)
		{
			SuggestCreatingGoals = suggestCreatingGoals, PausedGoalsMention = pausedMention, ActiveGoalWarning = activeWarning
		};
	}

	public Task<WeeklyReviewState> TransitionAsync(Guid reviewId, string action, string? localTime = null, CancellationToken ct = default)
	{
		var now = Now;
		return store.UpdateAsync((_, reviews) =>
		{
			var review = Find(reviews, reviewId);
			if (action is "complete" or "skip")
			{
				review.Status = action == "complete" ? "completed" : "skipped";
				review.DeferredUntilUtc = null;
				return review;
			}
			if (action != "defer") throw new ArgumentException("Action must be complete, skip or defer.");
			if (Terminal(review) || now >= review.EndsAtUtc) throw new ArgumentException("This review is finished or its target week has expired.");
			if (!DateTime.TryParseExact(localTime, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
				throw new ArgumentException("Resolve the requested deferral to a local yyyy-MM-ddTHH:mm time in the review timezone.");
			var due = WeeklyReviewCalendar.ToUtc(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), TimeZoneInfo.FindSystemTimeZoneById(review.TimeZoneId));
			if (due <= now || due >= review.EndsAtUtc) throw new ArgumentException("Defer to a future time before the target week ends.");
			review.Status = "deferred";
			review.DeferredUntilUtc = due;
			review.Occurrence++;
			review.Delivery = "pending";
			review.Attempts = 0;
			review.RetryAtUtc = null;
			review.OfferedInChat = false;
			return review;
		}, ct);
	}

	public async Task<string?> OfferAsync(CancellationToken ct = default)
	{
		var now = Now;
		var review = await store.UpdateAsync((preferences, reviews) =>
		{
			if (!preferences.Enabled || preferences.TimeZoneId is null) return null;
			var review = reviews.Where(r => r.Status == "deferred" && CanOffer(r, now))
				.OrderBy(r => r.DeferredUntilUtc).FirstOrDefault() ?? Resolve(preferences, reviews, now, false);
			return CanOffer(review, now) ? review : null;
		}, ct);
		if (review is null) return null;
		var report = await BuildReportAsync(review, now, 0, 1, ct);
		if (report.TotalCount == 0 && report.Goals.ActiveCount == 0) return null;
		return await store.UpdateAsync((preferences, reviews) =>
		{
			var current = Find(reviews, review.Id);
			if (!preferences.Enabled || current.Occurrence != review.Occurrence || !CanOffer(current, now)) return null;
			current.OfferedInChat = true;
			return WeeklyReviewCalendar.Notification(current.TargetWeek, report);
		}, ct);
	}

	public async Task<WeeklyReminderClaim?> ClaimReminderAsync(CancellationToken ct = default)
	{
		var now = Now;
		var review = await store.UpdateAsync((preferences, reviews) =>
		{
			if (!preferences.Enabled || preferences.TimeZoneId is null) return null;
			var review = reviews.Where(r => CanDeliver(r, now))
				.OrderBy(r => r.DeferredUntilUtc ?? r.ScheduledAtUtc).FirstOrDefault();
			if (review is null)
			{
				var candidate = WeeklyReviewCalendar.Create(preferences, now, true);
				if (now < candidate.ScheduledAtUtc || now >= candidate.StartsAtUtc) return null;
				review = Resolve(preferences, reviews, now, true);
			}
			return CanDeliver(review, now) ? review : null;
		}, ct);
		if (review is null) return null;
		var report = await BuildReportAsync(review, now, 0, 1, ct);
		if (report.TotalCount == 0 && report.Goals.ActiveCount == 0) return null;
		return await store.UpdateAsync((preferences, reviews) =>
		{
			var current = Find(reviews, review.Id);
			if (!preferences.Enabled || current.Occurrence != review.Occurrence || !CanDeliver(current, now)) return null;
			current.Delivery = "claimed";
			current.RetryAtUtc = now.AddMinutes(5);
			current.Attempts++;
			return new WeeklyReminderClaim(current.Id, current.Occurrence, WeeklyReviewCalendar.Notification(current.TargetWeek, report));
		}, ct);
	}

	public Task FinishDeliveryAsync(WeeklyReminderClaim claim, bool delivered, bool retryableRejection = false, CancellationToken ct = default) =>
		store.UpdateAsync((_, reviews) =>
		{
			var review = Find(reviews, claim.ReviewId);
			if (review.Occurrence != claim.Occurrence || review.Delivery != "claimed") return false;
			review.Delivery = delivered ? "delivered" : retryableRejection && review.Attempts < 3 ? "pending" : "failed";
			review.RetryAtUtc = Now.AddMinutes(15);
			return true;
		}, ct);

	private async Task<WeeklyReviewReport> BuildReportAsync(WeeklyReviewState review, DateTime now, int offset, int limit, CancellationToken ct)
	{
		var report = await store.ReportAsync(review, now, offset, limit, ct);
		var goals = WeeklyReviewGoalCalculator.Build(await store.GoalDataAsync(review, now, ct), review, now);
		return report with { Goals = goals };
	}

	private static string PausedMention(IReadOnlyList<WeeklyReviewPausedGoal> goals, DateTime now) =>
		$"{goals.Count} {(goals.Count == 1 ? "goal is" : "goals are")} paused: " +
		string.Join(", ", goals.Select(goal => $"\"{goal.Title}\" (paused {PauseAge(goal.PausedAt, now)})")) + ". Resume any of them?";

	private static string PauseAge(DateTime pausedAt, DateTime now)
	{
		var days = Math.Max(0, (int)(now - pausedAt).TotalDays);
		if (days == 0) return "today";
		var (count, unit) = days >= 30 ? (days / 30, "month") : days >= 7 ? (days / 7, "week") : (days, "day");
		return $"{count} {unit}{(count == 1 ? "" : "s")} ago";
	}

	private static WeeklyReviewState Find(IList<WeeklyReviewState> reviews, Guid id) =>
		reviews.FirstOrDefault(r => r.Id == id) ?? throw new KeyNotFoundException("Weekly review not found.");
	private static bool Terminal(WeeklyReviewState review) => review.Status is "completed" or "skipped";
	private static WeeklyReviewState? DueDeferral(IList<WeeklyReviewState> reviews, DateTime now) =>
		reviews.Where(r => r.Status == "deferred" && r.DeferredUntilUtc <= now && r.EndsAtUtc > now)
			.OrderBy(r => r.DeferredUntilUtc).FirstOrDefault();
	private static bool CanOffer(WeeklyReviewState review, DateTime now) =>
		!Terminal(review) && now < review.EndsAtUtc && !review.OfferedInChat && review.Delivery != "delivered" &&
		(review.Delivery != "claimed" || review.RetryAtUtc <= now) &&
		(review.DeferredUntilUtc.HasValue ? review.DeferredUntilUtc <= now : now >= review.StartsAtUtc);
	private static bool CanDeliver(WeeklyReviewState review, DateTime now) =>
		!Terminal(review) && !review.OfferedInChat && review.Delivery == "pending" && review.Attempts < 3 &&
		(review.RetryAtUtc is null || review.RetryAtUtc <= now) && now < review.EndsAtUtc &&
		(review.DeferredUntilUtc.HasValue ? review.DeferredUntilUtc <= now : now >= review.ScheduledAtUtc && now < review.StartsAtUtc);
	private static WeeklyReviewState Resolve(WeeklyReviewPreferences preferences, IList<WeeklyReviewState> reviews, DateTime now, bool scheduled)
	{
		var candidate = WeeklyReviewCalendar.Create(preferences, now, scheduled);
		// Freeze overlapping calendar periods across preference edits, including already finished reviews.
		var existing = reviews.OrderByDescending(r => r.TargetWeek)
			.FirstOrDefault(r => Math.Abs(r.TargetWeek.DayNumber - candidate.TargetWeek.DayNumber) < 7);
		if (existing is not null) return existing;
		reviews.Add(candidate);
		return candidate;
	}
}
