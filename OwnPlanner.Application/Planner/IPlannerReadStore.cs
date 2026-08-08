using OwnPlanner.Application.Common;

namespace OwnPlanner.Application.Planner;

/// <summary>
/// Reads planner collections and details from the current execution scope's tenant-bound store.
/// Implementations must apply filtering, ordering, counting, projection, and paging before
/// materializing collection results.
/// </summary>
public interface IPlannerReadStore
{
	Task<PagedResult<PlannerTaskSummaryDto>> QueryTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken = default);
	Task<PlannerTaskDetailDto?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PagedResult<PlannerGoalSummaryDto>> QueryGoalsAsync(PlannerGoalQuery query, CancellationToken cancellationToken = default);
	Task<PlannerGoalDetailDto?> GetGoalAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PagedResult<PlannerNoteSummaryDto>> QueryNotesAsync(PlannerNoteQuery query, CancellationToken cancellationToken = default);
	Task<PlannerNoteDetailDto?> GetNoteAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PlannerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}
