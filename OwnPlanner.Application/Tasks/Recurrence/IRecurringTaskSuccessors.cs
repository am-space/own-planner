using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public enum RecurringSuccessorKind { Replace, Resume }
public enum SuccessorConflictResolution { MoveStartAfterFacts, KeepStartAsSeparateWork }

/// <summary>Explicit intent and optional changes. Null rule/policy inherit; null end inherits unless ClearEndDate.</summary>
/// <remarks>Resume drops an inherited end before StartDate. Explicit backwards bounds always fail.</remarks>
public sealed record RecurringSuccessorRequest(RecurringSuccessorKind Kind, DateOnly StartDate,
	RecurrenceRule? Rule = null, MissedOccurrencePolicy? MissedPolicy = null,
	DateOnly? EndDate = null, bool ClearEndDate = false);

/// <summary>Read-only proposal with original facts. LaterSchedule is unavailable when moving beyond facts violates bounds.</summary>
/// <remarks>Any affected facts require an explicit choice; neither choice transfers or infers completed work.
/// PreviewToken is an opaque snapshot token, revalidated on apply; it is not an authorization credential.</remarks>
public sealed record RecurringSuccessorPreview(RecurringSeries Source, RecurrenceSchedule ProposedSchedule,
	IReadOnlyList<TaskOccurrenceFact> AffectedFacts, RecurrenceSchedule? LaterSchedule, string PreviewToken,
	RecurrenceSchedule PredecessorSchedule, RecurrenceSchedule? LaterPredecessorSchedule);

/// <summary>Stable identifiers returned for the first successful operation and every matching retry.</summary>
public sealed record RecurringSuccessorResult(Guid SourceTaskId, Guid SuccessorTaskId);

/// <summary>Persisted successful request fingerprint and result; scoped to the current user's database.</summary>
public sealed record RecurringSuccessorOperation(Guid RequestId, string RequestFingerprint, RecurringSuccessorResult Result);

/// <summary>Tenant-bound schedule replacement/resumption; no public creation or tenant selector.</summary>
public interface IRecurringTaskSuccessors
{
	/// <summary>Previews a chain-tip successor and all original facts on/after the requested start without lifecycle writes.</summary>
	Task<RecurringSuccessorPreview> PreviewAsync(Guid taskId, RecurringSuccessorRequest request, CancellationToken ct = default);
	/// <summary>Revalidates the preview and atomically bounds/links the predecessor, creates the successor and records the request.</summary>
	/// <remarks>Reuse the same nonempty requestId and identical inputs/token/choice for retries. A matching retry
	/// returns the first result; changed inputs, stale previews and existing successors fail without partial writes.
	/// Replace requires an open parent; Resume requires a completed parent and never reopens it.</remarks>
	Task<RecurringSuccessorResult> ApplyAsync(Guid taskId, Guid requestId, RecurringSuccessorRequest request,
		string previewToken, SuccessorConflictResolution? resolution = null, CancellationToken ct = default);
}
