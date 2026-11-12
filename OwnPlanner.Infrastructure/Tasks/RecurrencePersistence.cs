using System.Text.Json;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Infrastructure.Tasks;

/// <summary>Storage-only terminal fact; pending dates never have a row.</summary>
public sealed class TaskOccurrenceFactRow
{
	public Guid TaskId { get; set; }
	public DateOnly OccurrenceDate { get; set; }
	public TaskOccurrenceStatus Status { get; set; }
	public DateTime? CompletedAt { get; set; }

	public TaskOccurrenceFact ToFact() => new(new(TaskId, OccurrenceDate), Status, CompletedAt);
}

/// <summary>Explicit stable storage shape; dates use System.Text.Json's ISO DateOnly representation.</summary>
internal sealed record RecurrenceStorage(RecurrenceFrequency Frequency, int Interval, DayOfWeek WeekStart,
	DayOfWeek[] Weekdays, int[] MonthDays, DateOnly StartDate, DateOnly? EndDate, MissedOccurrencePolicy MissedPolicy)
{
	public static string Serialize(RecurrenceSchedule schedule) => JsonSerializer.Serialize(new RecurrenceStorage(
		schedule.Rule.Frequency, schedule.Rule.Interval, schedule.Rule.WeekStart, schedule.Rule.Weekdays.ToArray(),
		schedule.Rule.MonthDays.ToArray(), schedule.StartDate, schedule.EndDate, schedule.MissedPolicy));

	public static RecurrenceSchedule Deserialize(string json)
	{
		var row = JsonSerializer.Deserialize<RecurrenceStorage>(json) ?? throw new InvalidOperationException("Missing stored recurrence.");
		var rule = row.Frequency switch
		{
			RecurrenceFrequency.Weekly => RecurrenceRule.Weekly(row.Interval, row.Weekdays, row.WeekStart),
			RecurrenceFrequency.Monthly => RecurrenceRule.Monthly(row.Interval, row.MonthDays),
			_ => throw new InvalidOperationException("Invalid stored recurrence frequency.")
		};
		return new(rule, row.StartDate, row.EndDate, row.MissedPolicy);
	}
}
