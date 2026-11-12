using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Calculates scheduled dates without persistence, clock access, status projection or lifecycle writes.</summary>
public interface IRecurrenceCalculator
{
	/// <summary>Yields ordered distinct dates inside both the half-open range and inclusive series bounds.</summary>
	/// <remarks>The start date anchors intervals. Weekly rules use their captured week start; monthly
	/// rules clamp selected days independently each month. Caller supplies bounded ranges and handles
	/// completed/trashed parents and facts separately. Enumeration is lazy and never silently truncated.</remarks>
	IEnumerable<DateOnly> Enumerate(RecurrenceSchedule schedule, OccurrenceDateRange range);
}

public sealed class RecurrenceCalculator : IRecurrenceCalculator
{
	public IEnumerable<DateOnly> Enumerate(RecurrenceSchedule schedule, OccurrenceDateRange range)
	{
		ArgumentNullException.ThrowIfNull(schedule);
		ArgumentNullException.ThrowIfNull(range);
		var first = Math.Max(schedule.StartDate.DayNumber, range.StartDate.DayNumber);
		var end = Math.Min(range.EndExclusiveDate.DayNumber,
			schedule.EndDate?.DayNumber + 1 ?? range.EndExclusiveDate.DayNumber);
		if (first >= end) return [];
		return schedule.Rule.Frequency == RecurrenceFrequency.Weekly
			? EnumerateWeeks(schedule, first, end)
			: EnumerateMonths(schedule, first, end);
	}

	private static IEnumerable<DateOnly> EnumerateWeeks(RecurrenceSchedule schedule, int first, int end)
	{
		var rule = schedule.Rule;
		// Use day numbers so a first-year partial week can begin before DateOnly.MinValue.
		var anchor = schedule.StartDate.DayNumber - Offset(schedule.StartDate.DayOfWeek, rule.WeekStart);
		var weekIndex = (first - anchor) / 7;
		var activeWeek = (long)weekIndex + (rule.Interval - weekIndex % rule.Interval) % rule.Interval;
		for (var week = anchor + activeWeek * 7; week < end; week += (long)rule.Interval * 7)
		{
			foreach (var weekday in rule.Weekdays)
			{
				var day = week + Offset(weekday, rule.WeekStart);
				if (day >= first && day < end) yield return DateOnly.FromDayNumber((int)day);
			}
		}
	}

	private static IEnumerable<DateOnly> EnumerateMonths(RecurrenceSchedule schedule, int first, int end)
	{
		var rule = schedule.Rule;
		var firstDate = DateOnly.FromDayNumber(first);
		var anchor = MonthIndex(schedule.StartDate);
		var monthIndex = MonthIndex(firstDate);
		var elapsed = monthIndex - anchor;
		var activeMonth = (long)monthIndex + (rule.Interval - elapsed % rule.Interval) % rule.Interval;
		var lastMonth = MonthIndex(DateOnly.FromDayNumber(end - 1));
		for (var month = activeMonth; month <= lastMonth; month += rule.Interval)
		{
			var year = (int)(month / 12) + 1;
			var number = (int)(month % 12) + 1;
			var daysInMonth = DateTime.DaysInMonth(year, number);
			var previousDay = 0;
			foreach (var selectedDay in rule.MonthDays)
			{
				var day = Math.Min(selectedDay, daysInMonth);
				if (day == previousDay) continue;
				previousDay = day;
				var date = new DateOnly(year, number, day);
				if (date.DayNumber >= first && date.DayNumber < end) yield return date;
			}
		}
	}

	private static int Offset(DayOfWeek day, DayOfWeek weekStart) => (7 + (int)day - (int)weekStart) % 7;
	private static int MonthIndex(DateOnly date) => (date.Year - 1) * 12 + date.Month - 1;
}
