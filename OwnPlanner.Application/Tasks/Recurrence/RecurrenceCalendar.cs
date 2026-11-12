using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Full calendar period of an occurrence; a shortened series end never shortens this period.</summary>
public sealed record OccurrencePeriod(OccurrenceDateRange Dates, DateTime StartsAtUtc, DateTime EndsAtUtc)
{
	public bool IsExpired(DateTime asOfUtc)
	{
		if (asOfUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Expiry instant must be UTC.", nameof(asOfUtc));
		return asOfUtc >= EndsAtUtc;
	}
}

/// <summary>Read-only occurrence windows/expiry against one resolved shared user-calendar snapshot.</summary>
public static class RecurrenceCalendar
{
	/// <summary>Current and next calendar weeks/months coexist, including on the current period's final day.</summary>
	public static OccurrenceDateRange CurrentAndNext(RecurrenceFrequency frequency, PlanningPeriod calendar)
	{
		ArgumentNullException.ThrowIfNull(calendar);
		Validate(frequency, calendar.WeekStart);
		var start = PeriodStart(frequency, calendar.Today, calendar.WeekStart);
		return new(start, frequency == RecurrenceFrequency.Weekly ? start.AddDays(14) : start.AddMonths(2));
	}

	/// <summary>Uses current configured week start/timezone and CalendarRules DST conversion; never shifts occurrenceDate.</summary>
	public static OccurrencePeriod PeriodContaining(RecurrenceFrequency frequency, DateOnly occurrenceDate, PlanningPeriod calendar)
	{
		ArgumentNullException.ThrowIfNull(calendar);
		Validate(frequency, calendar.WeekStart);
		var start = PeriodStart(frequency, occurrenceDate, calendar.WeekStart);
		var end = frequency == RecurrenceFrequency.Weekly ? start.AddDays(7) : start.AddMonths(1);
		var zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone);
		return new(new(start, end), CalendarRules.ToUtc(start, TimeOnly.MinValue, zone), CalendarRules.ToUtc(end, TimeOnly.MinValue, zone));
	}

	private static DateOnly PeriodStart(RecurrenceFrequency frequency, DateOnly date, int weekStart) =>
		frequency == RecurrenceFrequency.Weekly ? CalendarRules.WeekStart(date, weekStart) : new(date.Year, date.Month, 1);

	private static void Validate(RecurrenceFrequency frequency, int weekStart)
	{
		if (!Enum.IsDefined(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency));
		if (weekStart is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(weekStart));
	}
}
