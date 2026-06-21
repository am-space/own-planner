namespace OwnPlanner.Application.Common;

/// <summary>
/// A single page of results plus the totals a caller needs to decide whether to keep paging.
/// Returned by list operations that page their output so large collections never serialize as one
/// unbounded payload.
/// </summary>
/// <param name="Items">The items on this page (at most <paramref name="Limit"/>).</param>
/// <param name="TotalCount">Total number of items across all pages for the same filter.</param>
/// <param name="Offset">The zero-based offset this page started at.</param>
/// <param name="Limit">The maximum page size that was applied.</param>
public sealed record PagedResult<T>(
	IReadOnlyList<T> Items,
	int TotalCount,
	int Offset,
	int Limit)
{
	/// <summary>True when more items exist beyond this page, i.e. the caller should request the next offset.</summary>
	public bool HasMore => Offset + Items.Count < TotalCount;
}
