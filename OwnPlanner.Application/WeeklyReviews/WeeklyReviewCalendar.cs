using System.Globalization;
using OwnPlanner.Domain.WeeklyReviews;

namespace OwnPlanner.Application.WeeklyReviews;

public static class WeeklyReviewCalendar
{
	public static void Validate(string? zone, int weekStart, string time, string channel, bool enabled)
	{
		if (weekStart is < 0 or > 6) throw new ArgumentException("Week start must be 0 (Sunday) through 6 (Saturday).");
		if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
			throw new ArgumentException("Reminder time must be HH:mm.");
		if (channel != "telegram") throw new ArgumentException("Telegram is the supported reminder channel.");
		if (string.IsNullOrWhiteSpace(zone))
		{
			if (enabled) throw new ArgumentException("Select your timezone before enabling weekly reminders.");
			return;
		}
		try { TimeZoneInfo.FindSystemTimeZoneById(zone); }
		catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
		{ throw new ArgumentException("Select a valid timezone."); }
	}

	public static DateOnly WeekStart(DateOnly date, int weekStart) => date.AddDays(-((7 + (int)date.DayOfWeek - weekStart) % 7));

	/// <summary>Advances nonexistent wall times to the first valid minute; ambiguous times select the earlier instant.</summary>
	public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
	{
		var local = date.ToDateTime(time, DateTimeKind.Unspecified);
		while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
		if (zone.IsAmbiguousTime(local))
			return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime;
		return TimeZoneInfo.ConvertTimeToUtc(local, zone);
	}

	public static WeeklyReviewState Create(WeeklyReviewPreferences preferences, DateTime nowUtc, bool scheduled)
	{
		if (string.IsNullOrWhiteSpace(preferences.TimeZoneId)) throw new ArgumentException("Select your timezone in weekly review settings first.");
		var zone = TimeZoneInfo.FindSystemTimeZoneById(preferences.TimeZoneId);
		var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
		var start = WeekStart(today, preferences.WeekStart);
		// On the last local day both manual review and proactive delivery refer to next week.
		var target = scheduled || today == start.AddDays(6) ? start.AddDays(7) : start;
		return new WeeklyReviewState
		{
			TargetWeek = target, TimeZoneId = preferences.TimeZoneId,
			StartsAtUtc = ToUtc(target, TimeOnly.MinValue, zone),
			EndsAtUtc = ToUtc(target.AddDays(7), TimeOnly.MinValue, zone),
			ScheduledAtUtc = ToUtc(target.AddDays(-1), TimeOnly.ParseExact(preferences.ReminderTime, "HH:mm", CultureInfo.InvariantCulture), zone)
		};
	}

	public static string Notification(DateOnly targetWeek, WeeklyReviewReport report) =>
		$"Ready for your weekly review for {targetWeek:yyyy-MM-dd}? {report.CarryoverCount} unfinished tasks are scheduled before the target week; {report.OverdueCount} tasks have overdue deadlines; {report.DueInTargetWeekCount} tasks are due in the target week. Ask to open the weekly review for {targetWeek:yyyy-MM-dd}, defer it, or skip it. No tasks have been changed.";
}
