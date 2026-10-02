using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Application.Calendar;

/// <summary>Resolved half-open date and instant ranges. Focus dates use the date range without timezone conversion.</summary>
public sealed record PlanningPeriod(string Name, DateTime AsOfUtc, string TimeZone, int WeekStart,
	DateOnly Today, DateOnly StartDate, DateOnly EndExclusiveDate, DateTime StartsAtUtc,
	DateTime EndsAtUtc, bool IsRolling, string? FallbackExplanation)
{
	public DateTime FocusStart => StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
	public DateTime FocusEnd => EndExclusiveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}

/// <summary>Reads host-bound calendar preferences and resolves a named period against the current clock without writing review state.</summary>
public interface IPlanningCalendar
{
	/// <summary>Resolves today, calendar weeks or explicit seven-day windows. Missing timezone uses a disclosed UTC fallback.</summary>
	Task<PlanningPeriod> ResolveAsync(string period, CancellationToken cancellationToken = default);
}

public sealed class PlanningCalendar(IWeeklyReviewStore store, TimeProvider clock) : IPlanningCalendar
{
	public async Task<PlanningPeriod> ResolveAsync(string period, CancellationToken cancellationToken = default)
	{
		PlanningCalendarResolver.Validate(period);
		var preferences = await store.GetPreferencesAsync(cancellationToken);
		return PlanningCalendarResolver.Resolve(period, clock.GetUtcNow().UtcDateTime, preferences.TimeZoneId, preferences.WeekStart);
	}
}

/// <summary>Shared calendar arithmetic; never infers the host timezone or changes weekly-review targeting.</summary>
public static class PlanningCalendarResolver
{
	public static void Validate(string period)
	{
		if (period is not ("today" or "thisWeek" or "remainderOfThisWeek" or "nextWeek" or "lastWeek" or "nextSevenDays" or "lastSevenDays"))
			throw new ArgumentException("calendarPeriod must be today, thisWeek, remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays or lastSevenDays.");
	}

	public static PlanningPeriod Resolve(string period, DateTime nowUtc, string? timeZoneId, int weekStart = 1)
	{
		Validate(period);
		if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Calendar clock instant must be UTC.");
		if (weekStart is < 0 or > 6) throw new ArgumentException("Week start must be 0 (Sunday) through 6 (Saturday).");
		var missing = string.IsNullOrWhiteSpace(timeZoneId);
		var zone = TimeZoneInfo.FindSystemTimeZoneById(missing ? "UTC" : timeZoneId!);
		var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
		var week = CalendarRules.WeekStart(today, weekStart);
		var start = period switch
		{
			"thisWeek" => week,
			"nextWeek" => week.AddDays(7),
			"lastWeek" => week.AddDays(-7),
			"lastSevenDays" => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc.AddDays(-7), zone)),
			_ => today
		};
		var end = period switch
		{
			"today" => today.AddDays(1),
			"remainderOfThisWeek" => week.AddDays(7),
			"lastSevenDays" => today.AddDays(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).TimeOfDay == TimeSpan.Zero ? 0 : 1),
			_ => start.AddDays(7)
		};
		var rolling = period == "lastSevenDays";
		return new(period, nowUtc, zone.Id, weekStart, today, start, end,
			rolling ? nowUtc.AddDays(-7) : CalendarRules.ToUtc(start, TimeOnly.MinValue, zone),
			rolling ? nowUtc : CalendarRules.ToUtc(end, TimeOnly.MinValue, zone), rolling,
			missing ? $"No timezone is configured. Using UTC and {(DayOfWeek)weekStart}-start weeks; select your timezone in weekly review settings. Reminders may stay disabled." : null);
	}
}

/// <summary>Local calendar helpers shared by ordinary reports and the frozen weekly-review workflow.</summary>
public static class CalendarRules
{
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
}
