using OwnPlanner.Application.Common;

namespace OwnPlanner.Application.Planner;

/// <summary>
/// Provides validated, read-only access to the authenticated user's planner workspace.
/// </summary>
public interface IPlannerReadService
{
	Task<PagedResult<PlannerTaskSummaryDto>> QueryTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken = default);
	Task<PlannerTaskDetailDto?> GetTaskAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PagedResult<PlannerGoalSummaryDto>> QueryGoalsAsync(PlannerGoalQuery query, CancellationToken cancellationToken = default);
	Task<PlannerGoalDetailDto?> GetGoalAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PagedResult<PlannerNoteSummaryDto>> QueryNotesAsync(PlannerNoteQuery query, CancellationToken cancellationToken = default);
	Task<PlannerNoteDetailDto?> GetNoteAsync(Guid id, CancellationToken cancellationToken = default);
	Task<PlannerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}
