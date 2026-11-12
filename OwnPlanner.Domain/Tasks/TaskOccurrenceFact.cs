namespace OwnPlanner.Domain.Tasks;

/// <summary>Original series ID and scheduled date, including facts preserved outside shortened bounds.</summary>
public sealed record TaskOccurrenceId
{
	public Guid TaskId { get; }
	public DateOnly OccurrenceDate { get; }

	public TaskOccurrenceId(Guid taskId, DateOnly occurrenceDate)
	{
		if (taskId == Guid.Empty) throw new ArgumentException("Task ID is required.", nameof(taskId));
		TaskId = taskId;
		OccurrenceDate = occurrenceDate;
	}
}

public enum TaskOccurrenceStatus
{
	Completed,
	Skipped,
	Cancelled
}

/// <summary>Recorded occurrence outcome. Pending occurrences are calculated and have no fact.</summary>
public sealed record TaskOccurrenceFact
{
	public TaskOccurrenceId Id { get; }
	public TaskOccurrenceStatus Status { get; }
	public DateTime? CompletedAt { get; }

	public TaskOccurrenceFact(TaskOccurrenceId id, TaskOccurrenceStatus status, DateTime? completedAt = null)
	{
		ArgumentNullException.ThrowIfNull(id);
		if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
		if ((status == TaskOccurrenceStatus.Completed) != completedAt.HasValue)
			throw new ArgumentException("Only completed facts require a completion instant.", nameof(completedAt));
		if (completedAt.HasValue && completedAt.Value.Kind != DateTimeKind.Utc)
			throw new ArgumentException("Completion instant must be UTC.", nameof(completedAt));
		Id = id;
		Status = status;
		CompletedAt = completedAt;
	}
}
