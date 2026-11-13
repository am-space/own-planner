using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Tenant-bound persistence boundary for atomic series/fact operations; no tenant arguments.</summary>
public interface IRecurringTaskStore
{
	/// <summary>Snapshots IDs of non-trashed open series, including archived lists whose schedules still run.</summary>
	Task<IReadOnlyList<Guid>> ListOpenSeriesIdsAsync(CancellationToken ct = default);
	/// <summary>Reads a consistent parent/list/fact snapshot without saving or invoking reconciliation.</summary>
	Task<T> ReadAsync<T>(Guid taskId, Func<IRecurringTaskSnapshot, CancellationToken, Task<T>> read, CancellationToken ct = default);
	/// <summary>Serializes current eligibility, fact transitions, checkpoints and closure in one write transaction.</summary>
	/// <remarks>Missing tasks throw KeyNotFoundException. Callback exceptions roll back every change.
	/// The transaction must not escape the callback; all methods use the caller's cancellation token.</remarks>
	Task<T> ExecuteAsync<T>(Guid taskId, Func<IRecurringTaskTransaction, CancellationToken, Task<T>> work, CancellationToken ct = default);
}

/// <summary>Current series state and terminal facts inside one consistent tenant-bound database snapshot.</summary>
public interface IRecurringTaskSnapshot
{
	/// <summary>Current task; domain changes persist only inside ExecuteAsync's write transaction.</summary>
	TaskItem Task { get; }
	/// <summary>Whether the task's current list exists and is not archived; read under the write lock.</summary>
	bool ListAvailable { get; }
	/// <summary>Reads one original scheduled date, including historical facts outside generation bounds.</summary>
	Task<TaskOccurrenceFact?> GetFactAsync(DateOnly date, CancellationToken ct);
	/// <summary>Returns all original facts on/after a cutoff, ordered by scheduled date, including out-of-bounds history.</summary>
	Task<IReadOnlyList<TaskOccurrenceFact>> GetFactsOnOrAfterAsync(DateOnly startDate, CancellationToken ct);
}

/// <summary>Writes that commit together with the current series snapshot.</summary>
public interface IRecurringTaskTransaction : IRecurringTaskSnapshot
{
	/// <summary>Inserts or explicitly replaces a fact already checked by Application policy.</summary>
	Task PutFactAsync(TaskOccurrenceFact fact, CancellationToken ct);
	/// <summary>Looks up a successful request key across this tenant, including a key originally used on another task.</summary>
	Task<RecurringSuccessorOperation?> GetSuccessorOperationAsync(Guid requestId, CancellationToken ct);
	/// <summary>Stages a linked successor and successful request; both commit with the predecessor or roll back together.</summary>
	void AddSuccessor(TaskItem successor, RecurringSuccessorOperation operation);
}
