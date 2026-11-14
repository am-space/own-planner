using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.WeeklyReviews;

/// <summary>One original occurrence and an opaque stale-state guard, never a parent completion target.</summary>
public sealed record WeeklyReviewOccurrence(PlanningWorkItem Work, DateTime Revision, string GuardToken);
public sealed record WeeklyReviewOccurrenceSource(Guid TaskId, string Title, Guid TaskListId, Guid? GoalId,
	DateTime? DueAt, RecurrenceSchedule Schedule, Guid? PredecessorTaskId, Guid? SuccessorTaskId);

/// <summary>Independent exact pending-occurrence page, ordered by original date then task ID.
/// A targeted historical fact lookup returns that fact regardless of current generation bounds.</summary>
public sealed record WeeklyReviewOccurrencePage(Guid ReviewId, PlanningPeriod Calendar, OccurrenceDateRange Range,
	int TotalCount, int SeriesCount, int CarryoverCount, int PlannedCount, int Offset, int Limit,
	IReadOnlyList<WeeklyReviewOccurrence> Items, IReadOnlyList<WeeklyReviewOccurrenceSource> Sources)
{
	public bool HasMore => (long)Offset + Items.Count < TotalCount;
	public string SelectionSemantics => "Pending current-period and target-week occurrences plus the complete retained prefix; carryover means original date before target week, never an overdue deadline. PlannedCount counts pending original dates inside target week. Counts precede paging. Targeted historical facts retain original identity and actual CompletedAt. Source deadlines apply once per series. Refresh GuardToken before explicitly authorized completeOccurrence/cancelOccurrence; focus actions never reschedule occurrences.";
}

/// <summary>Shared projection, frozen review calendar and opaque optimistic guard construction.</summary>
public static class WeeklyReviewOccurrences
{
	public static PlanningPeriod Calendar(WeeklyReviewState review, DateTime nowUtc)
	{
		var current = PlanningCalendarResolver.Resolve("today", nowUtc, review.TimeZoneId, (int)review.TargetWeek.DayOfWeek);
		return current with { Name = "weeklyReview", StartDate = review.TargetWeek,
			EndExclusiveDate = review.TargetWeek.AddDays(7), StartsAtUtc = DateTime.SpecifyKind(review.StartsAtUtc, DateTimeKind.Utc), EndsAtUtc = DateTime.SpecifyKind(review.EndsAtUtc, DateTimeKind.Utc) };
	}

	public static OccurrenceDateRange Range(PlanningPeriod calendar)
	{
		var start = new[] { RecurrenceFrequency.Weekly, RecurrenceFrequency.Monthly }
			.Select(f => RecurrenceCalendar.PeriodContaining(f, calendar.Today, calendar).Dates.StartDate)
			.Append(calendar.StartDate).Min();
		return new(start, calendar.EndExclusiveDate);
	}

	public static string Guard(WeeklyReviewState review, DateTime revision, long recurrenceRevision,
		RecurrenceSchedule schedule, PlanningWorkItem item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			JsonSerializer.Serialize(new { review.Id, review.TargetWeek, review.TimeZoneId, StartsAtUtc = review.StartsAtUtc.Ticks, EndsAtUtc = review.EndsAtUtc.Ticks,
				Revision = revision.Ticks, recurrenceRevision, schedule, item }))));

	public static async Task<WeeklyReviewOccurrencePage> ReadAsync(IPlanningWorkSnapshot snapshot, WeeklyReviewState review,
		DateTime nowUtc, IReadOnlyDictionary<Guid, (DateTime Revision, long RecurrenceRevision)> revisions,
		Guid? taskId = null, int offset = 0, int limit = 20, DateOnly? occurrenceDate = null,
		Action<PlanningWorkItem, PlanningWorkSource>? visit = null, CancellationToken ct = default)
	{
		if (offset < 0 || limit is < 1 or > 50) throw new ArgumentException("Use offset >= 0 and limit between 1 and 50.");
		if (occurrenceDate.HasValue && !taskId.HasValue) throw new ArgumentException("A targeted occurrence date requires taskId.");
		var calendar = Calendar(review, nowUtc);
		var range = Range(calendar);
		var page = new List<WeeklyReviewOccurrence>();
		var sources = new Dictionary<Guid, WeeklyReviewOccurrenceSource>();
		var series = new HashSet<Guid>();
		int total = 0, carryover = 0, planned = 0;
		// Facts are independent of current rule bounds and parent closure. A skipped historical
		// fact is reachable for an explicit correction; arbitrary dates never fabricate history.
		if (occurrenceDate is { } date && await snapshot.GetFactAsync(new(taskId!.Value, date), ct) is { } fact)
		{
			var source = (await snapshot.GetSourcesAsync(true, ct)).Single(s => s.TaskId == taskId);
			Add(new(source.TaskId, PlanningWorkKind.Occurrence, date, date,
				RecurrenceCalendar.PeriodContaining(source.Schedule!.Rule.Frequency, date, calendar),
				fact.Status switch { TaskOccurrenceStatus.Completed => PlanningWorkStatus.Completed,
					TaskOccurrenceStatus.Skipped => PlanningWorkStatus.Skipped, _ => PlanningWorkStatus.Cancelled }, fact.CompletedAt), source);
		}
		else
		{
			var queryRange = range;
			if (occurrenceDate is { } target)
			{
				var source = (await snapshot.GetSourcesAsync(false, ct)).SingleOrDefault(s => s.TaskId == taskId);
				if (target >= range.EndExclusiveDate || source?.Schedule is null ||
					target < range.StartDate && source.Schedule.MissedPolicy != MissedOccurrencePolicy.RetainEachObligation)
					return new(review.Id, calendar, range, 0, 0, 0, 0, offset, limit, [], []);
				// A targeted action must not scan a decades-long retained prefix under the writer lock.
				queryRange = new(target, target.AddDays(1));
			}
			await PlanningWorkReader.VisitAsync(new(queryRange, IncludeRetainedBeforeStart: occurrenceDate is null,
				OrdinaryTasks: OrdinaryTaskSelection.None, Kind: PlanningWorkKind.Occurrence, TaskId: taskId),
				calendar, snapshot, new RecurrenceCalculator(), (item, source) =>
				{
					if (occurrenceDate is null || item.OccurrenceDate == occurrenceDate) Add(item, source);
				}, ct);
		}
		return new(review.Id, calendar, range, total, series.Count, carryover, planned, offset, limit, page,
			sources.Values.OrderBy(s => s.TaskId).ToArray());

		void Add(PlanningWorkItem item, PlanningWorkSource source)
		{
			visit?.Invoke(item, source);
			series.Add(source.TaskId);
			if (item.Status == PlanningWorkStatus.Pending)
			{
				if (item.OccurrenceDate < review.TargetWeek) carryover = checked(carryover + 1);
				else if (item.OccurrenceDate < calendar.EndExclusiveDate) planned = checked(planned + 1);
			}
			var index = total; total = checked(total + 1);
			if (index < offset || page.Count >= limit) return;
			var revision = revisions[source.TaskId];
			page.Add(new(item, revision.Revision, Guard(review, revision.Revision, revision.RecurrenceRevision, source.Schedule!, item)));
			sources[source.TaskId] = new(source.TaskId, source.Title, source.TaskListId, source.GoalId, source.DueAt,
				source.Schedule!, source.PredecessorTaskId, source.SuccessorTaskId);
		}
	}
}
