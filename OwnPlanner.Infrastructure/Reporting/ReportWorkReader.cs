using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Tasks;

namespace OwnPlanner.Infrastructure.Reporting;

/// <summary>Uses the report's existing tenant-bound transaction and resolved calendar.</summary>
internal static class ReportWorkReader
{
	public static async Task<ReportWork> ReadAsync(AppDbContext db, PlanningPeriod calendar,
		OccurrenceDateRange range, DateTime activityStartUtc, DateTime activityEndUtc, int sampleLimit,
		CancellationToken ct, bool includeGroups = true)
	{
		var zone = TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone);
		var focusStart = range.StartDate.ToDateTime(TimeOnly.MinValue);
		var focusEnd = range.EndExclusiveDate.ToDateTime(TimeOnly.MinValue);
		var dueStart = CalendarRules.ToUtc(range.StartDate, TimeOnly.MinValue, zone);
		var dueEnd = CalendarRules.ToUtc(range.EndExclusiveDate, TimeOnly.MinValue, zone);
		var completed = await db.TaskItems.AsNoTracking()
			.Where(t => t.TrashedAt == null && t.Recurrence == null && t.IsCompleted && t.CompletedAt != null
				&& db.TaskLists.Any(l => l.Id == t.TaskListId && !l.IsArchived)
				&& (t.CompletedAt >= activityStartUtc && t.CompletedAt < activityEndUtc
					|| t.FocusAt >= focusStart && t.FocusAt < focusEnd || t.DueAt >= dueStart && t.DueAt < dueEnd))
			.Select(t => new { t.Id, t.Title, t.TaskListId, t.GoalId, t.IsImportant, t.FocusAt, t.DueAt, t.CompletedAt,
				ContextId = db.TaskLists.Where(l => l.Id == t.TaskListId).Select(l => l.ContextId).FirstOrDefault() })
			.ToArrayAsync(ct);
		var ordinary = completed.Select(t => new ReportOrdinaryCompletion(new(t.Id, t.Title, null,
			t.TaskListId, t.ContextId, t.GoalId, t.IsImportant, true, t.FocusAt,
			t.DueAt is { } due ? DateTime.SpecifyKind(due, DateTimeKind.Utc) : null, null, null, null),
			DateTime.SpecifyKind(t.CompletedAt!.Value, DateTimeKind.Utc))).ToArray();
		return await ReportWorkBuilder.BuildAsync(new PlanningWorkStore.Snapshot(db, readDescriptions: false), calendar, range,
			activityStartUtc, activityEndUtc, ordinary, sampleLimit, new RecurrenceCalculator(), ct, includeGroups);
	}

	public static PlanningPeriod UtcCalendar(DateTime asOfUtc) => PlanningCalendarResolver.Resolve("today", asOfUtc, "UTC");
}
