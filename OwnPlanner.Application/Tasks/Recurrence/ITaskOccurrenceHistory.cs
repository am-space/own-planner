using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Read-only, tenant-bound access to recorded facts independently of generation rules.</summary>
/// <remarks>Implementations retain facts outside shortened
/// bounds and on completed/predecessor series, subject to existing Trash/list access restrictions.
/// No query reconciles expiry, writes facts or closes a parent. Counts and pages use one read snapshot.</remarks>
public interface ITaskOccurrenceHistory
{
	/// <summary>Returns recorded facts, scheduled periods, source metadata and the resolved calendar.
	/// Exactly one bounded scheduled-date or completion-time selection is required. Order is scheduled
	/// date then task ID, or CompletedAt then task ID then scheduled date, according to the basis.
	/// Maximum page size is 100; maximum selection span is 3,660 days. No reconciliation occurs.</summary>
	Task<OccurrenceHistoryPage> QueryAsync(OccurrenceHistoryQuery query, CancellationToken ct = default);

	/// <summary>Gets a recorded fact by original identity, or null when unavailable; never regenerates it from a rule.</summary>
	Task<TaskOccurrenceFact?> GetAsync(TaskOccurrenceId id, CancellationToken ct = default);

	/// <summary>Returns facts selected by original scheduled date in a bounded half-open calendar range.</summary>
	/// <remarks>Optional taskId selects a current-tenant series. Order is occurrence date then task ID.
	/// Offset is nonnegative and limit is positive; implementations enforce a documented maximum limit.
	/// TotalCount counts all matching facts before paging, with each composite identity counted once.</remarks>
	Task<(IReadOnlyList<TaskOccurrenceFact> Items, int TotalCount)> ListByScheduledDateAsync(
		OccurrenceDateRange range, Guid? taskId, int offset, int limit, CancellationToken ct = default);

	/// <summary>Returns completed facts selected by actual CompletedAt in [startUtc, endExclusiveUtc).</summary>
	/// <remarks>Both boundaries must be UTC and the end must be after the start. Resolve local boundaries
	/// through the shared user calendar before calling. Order is CompletedAt, task ID, then occurrence date.
	/// Uses the same tenant, pagination and exact-count contracts as ListByScheduledDateAsync.
	/// Parent completion is excluded; scheduled dates are never interpreted as completion instants.</remarks>
	Task<(IReadOnlyList<TaskOccurrenceFact> Items, int TotalCount)> ListByCompletionAsync(
		DateTime startUtc, DateTime endExclusiveUtc, Guid? taskId, int offset, int limit, CancellationToken ct = default);
}
