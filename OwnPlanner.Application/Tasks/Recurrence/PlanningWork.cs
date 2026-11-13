using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public enum PlanningWorkKind { OrdinaryTask, Occurrence }
public enum PlanningWorkStatus { Pending, Completed, Skipped, Cancelled }
public enum OrdinaryTaskSelection { FocusOrDeadlineInRange, FocusInRange, DeadlineInRange, AllOpen, None }
public enum OccurrenceHistoryBasis { ScheduledDate, CompletionTime }

/// <summary>Explicit bounded selection. Null status includes every effective status within the window.</summary>
/// <remarks>Before-window retention adds only unresolved retain-policy occurrences. Ordinary selection
/// never expands a series into a parent work row. All filters apply to the combined work stream.</remarks>
public sealed record PlanningWorkQuery(OccurrenceDateRange Range,
	bool IncludeRetainedBeforeStart = false,
	OrdinaryTaskSelection OrdinaryTasks = OrdinaryTaskSelection.FocusOrDeadlineInRange,
	PlanningWorkStatus? Status = PlanningWorkStatus.Pending, PlanningWorkKind? Kind = null,
	Guid? TaskId = null, Guid? TaskListId = null, Guid? GoalId = null, Guid? ContextId = null,
	bool ImportantOnly = false, string? Search = null, int Offset = 0, int Limit = 25);

/// <summary>Current task metadata, once per distinct source in the returned page. Dates and deadlines
/// belong to the source, never to every occurrence. DueAt is a UTC instant; FocusAt is a calendar
/// focus value. Assignments are current state, not event history.</summary>
public sealed record PlanningWorkSource(Guid TaskId, string Title, string? Description,
	Guid TaskListId, Guid? ContextId, Guid? GoalId, bool IsImportant, bool IsCompleted,
	DateTime? FocusAt, DateTime? DueAt, RecurrenceSchedule? Schedule,
	Guid? PredecessorTaskId, Guid? SuccessorTaskId);

/// <summary>An ordinary identity is TaskId alone; an occurrence identity is (TaskId, OccurrenceDate).
/// WorkDate is a sorting/selection date, never a generated deadline or completion instant.</summary>
public sealed record PlanningWorkItem(Guid TaskId, PlanningWorkKind Kind, DateOnly? WorkDate,
	DateOnly? OccurrenceDate, OccurrencePeriod? Period, PlanningWorkStatus Status, DateTime? CompletedAt);

/// <summary>Exact filtered totals before paging. A series is counted once even when it has many work items.</summary>
public sealed record PlanningWorkCounts(int OrdinaryTaskCount, int SeriesCount, int OccurrenceCount)
{
	public int TaskCount => checked(OrdinaryTaskCount + SeriesCount);
	public int WorkItemCount => checked(OrdinaryTaskCount + OccurrenceCount);
}

public sealed record PlanningWorkPage(IReadOnlyList<PlanningWorkItem> Items,
	IReadOnlyList<PlanningWorkSource> Sources, PlanningWorkCounts Counts, int Offset, int Limit,
	PlanningPeriod Calendar, OccurrenceDateRange Range, bool IncludeRetainedBeforeStart)
{
	public int TotalCount => Counts.WorkItemCount;
	public bool HasMore => (long)Offset + Items.Count < TotalCount;
}

/// <summary>Exactly one selection basis and its boundaries are required; the other boundaries must be null.</summary>
public sealed record OccurrenceHistoryQuery(OccurrenceHistoryBasis Basis,
	OccurrenceDateRange? ScheduledRange = null, DateTime? StartUtc = null, DateTime? EndExclusiveUtc = null,
	Guid? TaskId = null, int Offset = 0, int Limit = 25);

/// <summary>Recorded fact plus its full scheduled period under the returned calendar; no expected work is inferred.</summary>
public sealed record OccurrenceHistoryItem(TaskOccurrenceFact Fact, OccurrencePeriod Period);

public sealed record OccurrenceHistoryPage(IReadOnlyList<OccurrenceHistoryItem> Items,
	IReadOnlyList<PlanningWorkSource> Sources, int TotalCount, int Offset, int Limit,
	PlanningPeriod Calendar, OccurrenceHistoryQuery Selection)
{
	public bool HasMore => (long)Offset + Items.Count < TotalCount;
}

/// <summary>Shared internal planning read model, resolved through the host's authenticated tenant.</summary>
public interface IPlanningWorkReader
{
	/// <summary>Combines ordinary work and computed occurrences before filtering/counting/paging.
	/// Order is WorkDate (null last), TaskId, then kind. One calendar and database snapshot per call;
	/// subsequent pages can reflect later writes. Never reconciles, saves or creates pending rows.</summary>
	Task<PlanningWorkPage> QueryAsync(PlanningWorkQuery query, CancellationToken ct = default);
}

public static class RecurrenceReadLimits
{
	public const int MaximumPageSize = 100;
	public const int MaximumRangeDays = 3660;

	public static void ValidatePage(int offset, int limit)
	{
		if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
		if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
	}

	public static void ValidateRange(OccurrenceDateRange range)
	{
		ArgumentNullException.ThrowIfNull(range);
		if (range.EndExclusiveDate.DayNumber - range.StartDate.DayNumber > MaximumRangeDays)
			throw new ArgumentOutOfRangeException(nameof(range), $"Select at most {MaximumRangeDays} calendar days.");
	}
}
