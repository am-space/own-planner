using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Future tenant-bound lifecycle port. Stage 1 defines the contract; no implementation is registered.</summary>
/// <remarks>Implementations must authorize against the current tenant and validate parent/list eligibility.
/// Writes and fact-versus-expiry races are atomic. No argument selects a user or database.
/// Read paths never invoke this port implicitly. See docs/recurring-tasks.md for transaction boundaries.</remarks>
public interface IRecurringTaskLifecycle
{
	/// <summary>Completes one eligible occurrence or explicitly corrects a skipped fact, retaining its original identity.</summary>
	/// <remarks>Null completedAtUtc uses the injected clock. A supplied historical instant must be UTC
	/// and no later than that clock snapshot. Retries preserve
	/// the first successful CompletedAt. Historical correction remains possible on a closed/predecessor
	/// series without reopening it. This operation never completes the parent or transfers successor work.</remarks>
	Task<TaskOccurrenceFact> CompleteOccurrenceAsync(TaskOccurrenceId id, DateTime? completedAtUtc = null, CancellationToken ct = default);

	/// <summary>Records cancellation of one eligible occurrence without terminating the series.</summary>
	/// <remarks>Retry-safe; preserves existing completion facts and does not transfer cancellation to a successor.</remarks>
	Task<TaskOccurrenceFact> CancelOccurrenceAsync(TaskOccurrenceId id, CancellationToken ct = default);

	/// <summary>Explicitly ends a recurring parent, suppressing future generation while preserving its history.</summary>
	/// <remarks>Retry-safe. Parent completion is administrative and never contributes an occurrence completion.</remarks>
	Task EndSeriesAsync(Guid taskId, CancellationToken ct = default);

	/// <summary>Catches up expired periods and bounded-series closure using one current clock/calendar snapshot.</summary>
	/// <remarks>Idempotent and retry-safe after downtime; preserves completed/cancelled facts and corrected
	/// skipped facts. Does not close unbounded series or bounded series with unresolved obligations.
	/// Host lifecycle/background execution is independent of enabled reminders; queries remain read-only.</remarks>
	Task ReconcileAsync(CancellationToken ct = default);
}
