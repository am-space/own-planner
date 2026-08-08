using OwnPlanner.Application.Common;

namespace OwnPlanner.Application.Planner;

public sealed class PlannerReadService(IPlannerReadStore store) : IPlannerReadService
{
	public Task<PagedResult<PlannerTaskSummaryDto>> QueryTasksAsync(
		PlannerTaskQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ValidatePaging(query.Offset, query.Limit);
		ValidateEnum(query.Status, nameof(query.Status));
		return store.QueryTasksAsync(query with { Search = NormalizeSearch(query.Search) }, cancellationToken);
	}

	public Task<PlannerTaskDetailDto?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default)
	{
		ValidateId(id);
		return store.GetTaskAsync(id, cancellationToken);
	}

	public Task<PagedResult<PlannerGoalSummaryDto>> QueryGoalsAsync(
		PlannerGoalQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ValidatePaging(query.Offset, query.Limit);
		ValidateEnum(query.Status, nameof(query.Status));
		if (query.Horizon.HasValue)
		{
			ValidateEnum(query.Horizon.Value, nameof(query.Horizon));
		}

		return store.QueryGoalsAsync(query with { Search = NormalizeSearch(query.Search) }, cancellationToken);
	}

	public Task<PlannerGoalDetailDto?> GetGoalAsync(Guid id, CancellationToken cancellationToken = default)
	{
		ValidateId(id);
		return store.GetGoalAsync(id, cancellationToken);
	}

	public Task<PagedResult<PlannerNoteSummaryDto>> QueryNotesAsync(
		PlannerNoteQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ValidatePaging(query.Offset, query.Limit);
		return store.QueryNotesAsync(query with { Search = NormalizeSearch(query.Search) }, cancellationToken);
	}

	public Task<PlannerNoteDetailDto?> GetNoteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		ValidateId(id);
		return store.GetNoteAsync(id, cancellationToken);
	}

	public Task<PlannerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
		=> store.GetFilterOptionsAsync(cancellationToken);

	private static string? NormalizeSearch(string? search)
		=> string.IsNullOrWhiteSpace(search) ? null : search.Trim();

	private static void ValidatePaging(int offset, int limit)
	{
		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
		}

		if (limit < 1 || limit > PlannerReadDefaults.MaximumPageSize)
		{
			throw new ArgumentOutOfRangeException(
				nameof(limit),
				$"Limit must be between 1 and {PlannerReadDefaults.MaximumPageSize}.");
		}
	}

	private static void ValidateId(Guid id)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException("Item id is required.", nameof(id));
		}
	}

	private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
		where TEnum : struct, Enum
	{
		if (!Enum.IsDefined(value))
		{
			throw new ArgumentOutOfRangeException(parameterName, value, "Unsupported filter value.");
		}
	}
}
