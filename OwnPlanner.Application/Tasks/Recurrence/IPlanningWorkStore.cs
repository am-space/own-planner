using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Executes read-only callbacks over one consistent, host-bound tenant database snapshot.</summary>
public interface IPlanningWorkStore
{
	/// <summary>Reads immutable task/fact data without SaveChanges, reconciliation or tenant selector arguments.</summary>
	Task<T> ReadAsync<T>(Func<IPlanningWorkSnapshot, CancellationToken, Task<T>> read, CancellationToken ct = default);
}

/// <summary>Sources exclude Trash and archived/missing lists. Closed series are available only when requested.</summary>
public interface IPlanningWorkSnapshot
{
	/// <summary>Returns current source metadata; includeCompleted permits historical closed/predecessor reads.</summary>
	Task<IReadOnlyList<PlanningWorkSource>> GetSourcesAsync(bool includeCompleted, CancellationToken ct);
	/// <summary>Reads recorded facts by original scheduled dates, independently of generation bounds or parent closure.</summary>
	Task<IReadOnlyList<TaskOccurrenceFact>> GetScheduledFactsAsync(OccurrenceDateRange range, Guid? taskId, CancellationToken ct);
	/// <summary>Reads completed facts by actual UTC instant in a half-open range, never parent completion.</summary>
	Task<IReadOnlyList<TaskOccurrenceFact>> GetCompletionFactsAsync(DateTime startUtc, DateTime endExclusiveUtc, Guid? taskId, CancellationToken ct);
	/// <summary>Reads one fact by original composite identity, retaining the same source eligibility restrictions.</summary>
	Task<TaskOccurrenceFact?> GetFactAsync(TaskOccurrenceId id, CancellationToken ct);
}
