using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Planner;

/// <summary>Read-only series presentation policy, shared by Planner summary and detail reads.</summary>
public static class PlannerRecurrenceProjection
{
	public static string Describe(RecurrenceRule rule) => rule.Frequency == RecurrenceFrequency.Weekly
		? $"Every {rule.Interval} {(rule.Interval == 1 ? "week" : "weeks")} on {string.Join(", ", rule.Weekdays)} (week starts {rule.WeekStart})"
		: $"Every {rule.Interval} {(rule.Interval == 1 ? "month" : "months")} on days {string.Join(", ", rule.MonthDays)} (clamped to month end)";

	/// <summary>Checks the complete shortened generation interval, without enumerating pending rows
	/// into storage. Closed/unavailable series have no actionable obligations. Every terminal fact
	/// resolves its original date; skip-policy dates expire only at the end of their full local period.</summary>
	public static bool HasOutstandingWork(RecurrenceSchedule schedule, bool isCompleted,
		Guid? successorId, IReadOnlySet<DateOnly> resolvedDates, PlanningPeriod calendar,
		IRecurrenceCalculator calculator, CancellationToken ct = default)
	{
		// A linked predecessor ends strictly before its successor's start, so EndDate + 1 is safe.
		if (isCompleted || successorId is null || schedule.EndDate is not { } end || schedule.IsEmpty) return false;
		var first = schedule.StartDate;
		if (schedule.MissedPolicy == MissedOccurrencePolicy.SkipExpiredPeriods)
		{
			var current = RecurrenceCalendar.PeriodContaining(schedule.Rule.Frequency, calendar.Today, calendar).Dates.StartDate;
			if (current > first) first = current;
		}
		if (first > end) return false;
		foreach (var date in calculator.Enumerate(schedule, new(first, end.AddDays(1))))
		{
			ct.ThrowIfCancellationRequested();
			if (resolvedDates.Contains(date)) continue;
			return true;
		}
		return false;
	}
}
