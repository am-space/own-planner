using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Tenant-bound atomic occurrence actions and explicit write reconciliation.</summary>
/// <remarks>Implementations must authorize against the current tenant and validate parent/list eligibility.
/// Writes and fact-versus-expiry races are atomic. No argument selects a user or database.
/// Read paths never invoke this port implicitly. See docs/recurring-tasks.md for transaction boundaries.</remarks>
public interface IRecurringTaskLifecycle
{
	/// <summary>Completes one eligible occurrence or explicitly corrects a skipped fact, retaining its original identity.</summary>
	/// <remarks>Null completedAtUtc uses the injected clock. A supplied historical instant must be UTC
	/// and no later than that clock snapshot. Retries preserve
	/// the first successful CompletedAt. Historical correction remains possible on a closed/predecessor
	/// series without reopening it. The fact transition never completes the parent or transfers successor work;
	/// explicit catch-up separately enforces the bounded-series administrative closure rule.</remarks>
	Task<TaskOccurrenceFact> CompleteOccurrenceAsync(TaskOccurrenceId id, DateTime? completedAtUtc = null, CancellationToken ct = default);

	/// <summary>Records cancellation of one eligible occurrence without terminating the series.</summary>
	/// <remarks>Retry-safe; preserves existing completion facts and does not transfer cancellation to a successor.</remarks>
	Task<TaskOccurrenceFact> CancelOccurrenceAsync(TaskOccurrenceId id, CancellationToken ct = default);

	/// <summary>Explicitly ends a recurring parent, suppressing future generation while preserving its history.</summary>
	/// <remarks>Retry-safe. Parent completion is administrative and never contributes an occurrence completion.</remarks>
	Task EndSeriesAsync(Guid taskId, CancellationToken ct = default);

	/// <summary>Catches up a series, then moves it to Trash and ends it without deleting its facts.</summary>
	/// <remarks>Retry-safe. Unlike occurrence actions, Trash remains available in an archived list.
	/// Restore keeps the series closed; resumption requires an explicit successor.</remarks>
	Task TrashSeriesAsync(Guid taskId, CancellationToken ct = default);

	/// <summary>Catches up expired periods and bounded-series closure using one current clock/calendar snapshot.</summary>
	/// <remarks>Idempotent and retry-safe after downtime; preserves completed/cancelled facts and corrected
	/// skipped facts. Does not close unbounded series or bounded series with unresolved obligations.
	/// Host lifecycle/background execution is independent of enabled reminders; queries remain read-only.</remarks>
	Task ReconcileAsync(CancellationToken ct = default);
}
