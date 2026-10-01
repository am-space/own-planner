using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class WeeklyReviewTools(IWeeklyReviewService service, WeeklyReviewActions actions)
{
	[McpServerTool(Name = "weekly_review_settings_get", ReadOnly = true, Idempotent = true), Description("Read weekly review preferences. If timezone is absent, ask the user to select one before configuring or opening a review.")]
	public async Task<object> Settings(CancellationToken ct = default) => await service.GetPreferencesAsync(ct);

	[McpServerTool(Name = "weekly_review_configure"), Description("Set explicit weekly reminder preferences only when requested. enabled opts in/out; timeZoneId is a user-selected timezone such as Europe/London; weekStart 0=Sunday..6=Saturday; reminderTime HH:mm on the last day of that week. Only telegram channel is supported. Defaults: Monday week, 18:00. Omitted preference fields preserve existing values. Disabling preserves preferences and review history.")]
	public async Task<object> Configure(bool enabled, string? timeZoneId = null, int? weekStart = null, string? reminderTime = null, string? channel = null, CancellationToken ct = default)
	{
		var current = await service.GetPreferencesAsync(ct);
		return await service.ConfigureAsync(enabled, timeZoneId ?? current.TimeZoneId, weekStart ?? current.WeekStart,
			reminderTime ?? current.ReminderTime, channel ?? current.Channel, ct);
	}

	[McpServerTool(Name = "weekly_review_open"), Description("Open/resume the shared weekly review. No planning-data mutation. Returns active goals first with calculated flags, completed/open task samples, target-week plan counts and goal-work share; then a bounded page of remaining carryover/overdue tasks. Page tasks with offset/limit (maximum 50). A goal task already planned for the target week is omitted from the task page. suggestCreatingGoals is true only on the first opening of a review with no active goals. Optional reviewId resumes that review; targetWeek yyyy-MM-dd selects the period in a reminder. Refresh before changes. Opening does not authorize any task or goal mutation.")]
	public async Task<object> Open(string? reviewId = null, int offset = 0, int limit = 20, CancellationToken ct = default, string? targetWeek = null)
	{
		DateOnly? target = null;
		if (targetWeek is not null)
		{
			if (!DateOnly.TryParseExact(targetWeek, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
				throw new ArgumentException("targetWeek must be yyyy-MM-dd from the reminder.");
			target = date;
		}
		return await service.OpenAsync(ParseId(reviewId, nameof(reviewId)), offset, limit, ct, target);
	}

	[McpServerTool(Name = "weekly_review_transition"), Description("On explicit user request, complete, skip, or defer a review by reviewId. Completing one task does not finish a review. For defer resolve a definite future localTime yyyy-MM-ddTHH:mm in the review's timezone, before its target week ends. Ask when ambiguous. 'Tomorrow' means the next local calendar day; retain the original target week. For disable reminders use weekly_review_configure with enabled=false and preserve existing preferences.")]
	public async Task<object> Transition(string reviewId, string action, string? localTime = null, CancellationToken ct = default) =>
		await service.TransitionAsync(ParseRequiredId(reviewId, nameof(reviewId)), action, localTime, ct);

	[McpServerTool(Name = "weekly_review_offer"), Description("After answering a suitable planning request, check once for an eligible missed weekly reminder or due deferral. Do not call during urgent/unrelated work or switch modes. Returns a counts-only invitation or no invitation; repeat only the returned invitation. This claims the offer across web/Telegram but never changes tasks. Delivered, finished, skipped, disabled and previously offered reviews are suppressed.")]
	public async Task<object> Offer(CancellationToken ct = default) => new { invitation = await service.OfferAsync(ct) };

	[McpServerTool(Name = "weekly_review_apply"), Description("Apply one explicitly authorized review task action with reviewId, taskId and revision from a fresh review result or targeted task lookup. Existing actions: focus, clearFocus, complete, trash, deadline, clearDeadline. goalFocus plans an existing task already linked to the selected active goal for focusDate in the target week, even when it is outside the ordinary task page; specify goalId. linkGoal links an unlinked task in the task review to an active goal only after user confirmation; specify goalId. Rechecks task, list, revision, goal and review; applied=false means refresh. Opening alone never authorizes mutations. Report partial failures; focus preserves deadlines.")]
	public async Task<object> Apply(string reviewId, string taskId, string revision, string action, string? focusDate = null, string? dueAt = null, CancellationToken ct = default, string? goalId = null)
	{
		if (!DateTimeOffset.TryParse(revision, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var version))
			throw new ArgumentException("revision must be the timestamp returned by the review.");
		DateOnly? focus = null;
		if (focusDate is not null)
		{
			if (!DateOnly.TryParseExact(focusDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
				throw new ArgumentException("focusDate must be yyyy-MM-dd.");
			focus = parsed;
		}
		DateTime? deadline = null;
		if (action == "deadline")
		{
			if (dueAt is null || !(dueAt.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(dueAt, @"[+-]\d{2}:\d{2}$")) ||
				!DateTimeOffset.TryParse(dueAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
				throw new ArgumentException("dueAt must include an explicit UTC offset or Z.");
			deadline = parsed.UtcDateTime;
		}
		return await actions.ApplyAsync(ParseRequiredId(reviewId, nameof(reviewId)), ParseRequiredId(taskId, nameof(taskId)), version.UtcDateTime, action, focus, deadline, ct,
			ParseId(goalId, nameof(goalId)));
	}

	private static Guid? ParseId(string? value, string parameterName) => value is null ? null :
		Guid.TryParse(value, out var id) ? id : throw new ArgumentException($"{parameterName} must be a UUID.", parameterName);

	private static Guid ParseRequiredId(string? value, string parameterName) =>
		ParseId(value, parameterName) ?? throw new ArgumentException($"{parameterName} is required.", parameterName);
}
