using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Tenant-bound persistence boundary for atomic series/fact operations; no tenant arguments.</summary>
public interface IRecurringTaskStore
{
	/// <summary>Snapshots IDs of non-trashed open series, including archived lists whose schedules still run.</summary>
	Task<IReadOnlyList<Guid>> ListOpenSeriesIdsAsync(CancellationToken ct = default);
	/// <summary>Serializes current eligibility, fact transitions, checkpoints and closure in one write transaction.</summary>
	/// <remarks>Missing tasks throw KeyNotFoundException. Callback exceptions roll back every change.
	/// The transaction must not escape the callback; all methods use the caller's cancellation token.</remarks>
	Task<T> ExecuteAsync<T>(Guid taskId, Func<IRecurringTaskTransaction, CancellationToken, Task<T>> work, CancellationToken ct = default);
}

/// <summary>Current series state and terminal facts inside one tenant-bound write transaction.</summary>
public interface IRecurringTaskTransaction
{
	/// <summary>Tracked task whose domain changes commit atomically with facts.</summary>
	TaskItem Task { get; }
	/// <summary>Whether the task's current list exists and is not archived; read under the write lock.</summary>
	bool ListAvailable { get; }
	/// <summary>Reads one original scheduled date, including historical facts outside generation bounds.</summary>
	Task<TaskOccurrenceFact?> GetFactAsync(DateOnly date, CancellationToken ct);
	/// <summary>Inserts or explicitly replaces a fact already checked by Application policy.</summary>
	Task PutFactAsync(TaskOccurrenceFact fact, CancellationToken ct);
}
