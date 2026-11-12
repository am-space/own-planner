namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Explicit bounded, nonempty calendar range [StartDate, EndExclusiveDate); never UTC instants.</summary>
public sealed record OccurrenceDateRange
{
	public DateOnly StartDate { get; }
	public DateOnly EndExclusiveDate { get; }

	public OccurrenceDateRange(DateOnly startDate, DateOnly endExclusiveDate)
	{
		if (endExclusiveDate <= startDate)
			throw new ArgumentException("Range end must be after its start.", nameof(endExclusiveDate));
		StartDate = startDate;
		EndExclusiveDate = endExclusiveDate;
	}
}
