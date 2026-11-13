namespace OwnPlanner.Domain.Tasks;

/// <summary>Series generation bounds and explicit missed-work policy. EndDate is inclusive.</summary>
public sealed record RecurrenceSchedule
{
	public RecurrenceRule Rule { get; }
	public DateOnly StartDate { get; }
	public DateOnly? EndDate { get; }
	public MissedOccurrencePolicy MissedPolicy { get; }
	/// <summary>A retired interval can be empty after an end-only schedule replacement.</summary>
	public bool IsEmpty => EndDate < StartDate;

	public RecurrenceSchedule(RecurrenceRule rule, DateOnly startDate, DateOnly? endDate, MissedOccurrencePolicy missedPolicy)
		: this(rule, startDate, endDate, missedPolicy, false) { }

	private RecurrenceSchedule(RecurrenceRule rule, DateOnly startDate, DateOnly? endDate,
		MissedOccurrencePolicy missedPolicy, bool retired)
	{
		ArgumentNullException.ThrowIfNull(rule);
		if (!retired && endDate < startDate) throw new ArgumentException("End date cannot precede start date.", nameof(endDate));
		if (!Enum.IsDefined(missedPolicy)) throw new ArgumentOutOfRangeException(nameof(missedPolicy));
		Rule = rule;
		StartDate = startDate;
		EndDate = endDate;
		MissedPolicy = missedPolicy;
	}

	/// <summary>Bounds generation before a successor without extending an earlier end or changing the rule/anchor.</summary>
	/// <remarks>Unlike creation, retiring an existing series may leave an empty interval.</remarks>
	public RecurrenceSchedule ShortenBefore(DateOnly successorStart)
	{
		if (successorStart == DateOnly.MinValue) throw new ArgumentOutOfRangeException(nameof(successorStart));
		var end = successorStart.AddDays(-1);
		return new(Rule, StartDate, EndDate is { } existing && existing < end ? existing : end, MissedPolicy, true);
	}
}

/// <summary>Read snapshot of one task series; linkage never transfers occurrence identity or facts.</summary>
public sealed record RecurringSeries
{
	public Guid TaskId { get; }
	public RecurrenceSchedule Schedule { get; }
	public bool IsCompleted { get; }
	public Guid? PredecessorTaskId { get; }
	public Guid? SuccessorTaskId { get; }

	public RecurringSeries(Guid taskId, RecurrenceSchedule schedule, bool isCompleted = false,
		Guid? predecessorTaskId = null, Guid? successorTaskId = null)
	{
		if (taskId == Guid.Empty) throw new ArgumentException("Task ID is required.", nameof(taskId));
		ArgumentNullException.ThrowIfNull(schedule);
		if (predecessorTaskId == Guid.Empty || predecessorTaskId == taskId)
			throw new ArgumentException("Predecessor must be another task.", nameof(predecessorTaskId));
		if (successorTaskId == Guid.Empty || successorTaskId == taskId ||
			(successorTaskId.HasValue && successorTaskId == predecessorTaskId))
			throw new ArgumentException("Successor must be another task distinct from the predecessor.", nameof(successorTaskId));
		TaskId = taskId;
		Schedule = schedule;
		IsCompleted = isCompleted;
		PredecessorTaskId = predecessorTaskId;
		SuccessorTaskId = successorTaskId;
	}
}
