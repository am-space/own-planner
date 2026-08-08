using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Planner;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Controllers;

/// <summary>
/// Authenticated, read-only access to the current user's planner workspace.
/// </summary>
[ApiController]
[Route("api/planner")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlannerController(
	IPlannerReadService plannerReadService,
	IPerUserAppInitializationService initializationService) : ControllerBase
{
	[HttpGet("tasks")]
	public async Task<ActionResult<PagedResult<PlannerTaskSummaryDto>>> GetTasks(
		[FromQuery] string? search = null,
		[FromQuery] PlannerTaskStatus status = PlannerTaskStatus.Open,
		[FromQuery] bool important = false,
		[FromQuery] Guid? taskListId = null,
		[FromQuery] Guid? contextId = null,
		[FromQuery] Guid? goalId = null,
		[FromQuery] int offset = 0,
		[FromQuery] int limit = PlannerReadDefaults.DefaultPageSize,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		return Ok(await plannerReadService.QueryTasksAsync(
			new PlannerTaskQuery(search, status, important, taskListId, contextId, goalId, offset, limit),
			cancellationToken));
	}

	[HttpGet("tasks/{id}")]
	public async Task<ActionResult<PlannerTaskDetailDto>> GetTask(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		var task = await plannerReadService.GetTaskAsync(id, cancellationToken);
		return task is null ? NotFound() : Ok(task);
	}

	[HttpGet("goals")]
	public async Task<ActionResult<PagedResult<PlannerGoalSummaryDto>>> GetGoals(
		[FromQuery] string? search = null,
		[FromQuery] PlannerGoalStatus status = PlannerGoalStatus.Active,
		[FromQuery] GoalHorizon? horizon = null,
		[FromQuery] int offset = 0,
		[FromQuery] int limit = PlannerReadDefaults.DefaultPageSize,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		return Ok(await plannerReadService.QueryGoalsAsync(
			new PlannerGoalQuery(search, status, horizon, offset, limit),
			cancellationToken));
	}

	[HttpGet("goals/{id}")]
	public async Task<ActionResult<PlannerGoalDetailDto>> GetGoal(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		var goal = await plannerReadService.GetGoalAsync(id, cancellationToken);
		return goal is null ? NotFound() : Ok(goal);
	}

	[HttpGet("notes")]
	public async Task<ActionResult<PagedResult<PlannerNoteSummaryDto>>> GetNotes(
		[FromQuery] string? search = null,
		[FromQuery] bool pinned = false,
		[FromQuery] Guid? noteListId = null,
		[FromQuery] Guid? contextId = null,
		[FromQuery] Guid? goalId = null,
		[FromQuery] int offset = 0,
		[FromQuery] int limit = PlannerReadDefaults.DefaultPageSize,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		return Ok(await plannerReadService.QueryNotesAsync(
			new PlannerNoteQuery(search, pinned, noteListId, contextId, goalId, offset, limit),
			cancellationToken));
	}

	[HttpGet("notes/{id}")]
	public async Task<ActionResult<PlannerNoteDetailDto>> GetNote(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		var note = await plannerReadService.GetNoteAsync(id, cancellationToken);
		return note is null ? NotFound() : Ok(note);
	}

	[HttpGet("filter-options")]
	public async Task<ActionResult<PlannerFilterOptionsDto>> GetFilterOptions(
		CancellationToken cancellationToken = default)
	{
		await EnsurePlannerInitializedAsync(cancellationToken);
		return Ok(await plannerReadService.GetFilterOptionsAsync(cancellationToken));
	}

	private Task EnsurePlannerInitializedAsync(CancellationToken cancellationToken)
		=> initializationService.EnsureInitializedAsync(
			User.GetRequiredPlannerSessionContext("planner"),
			cancellationToken);
}
