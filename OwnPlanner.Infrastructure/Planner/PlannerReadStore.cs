using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Planner;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Planner;

public sealed class PlannerReadStore(IPlannerDbContextFactory dbContextFactory) : IPlannerReadStore
{
	public async Task<PagedResult<PlannerTaskSummaryDto>> QueryTasksAsync(
		PlannerTaskQuery query,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var filtered =
			from task in db.TaskItems.AsNoTracking()
			join taskList in db.TaskLists.AsNoTracking() on task.TaskListId equals taskList.Id
			join planningContext in db.PlanningContexts.AsNoTracking()
				on taskList.ContextId equals (Guid?)planningContext.Id into planningContexts
			from planningContext in planningContexts.DefaultIfEmpty()
			join goal in db.Goals.AsNoTracking()
				on task.GoalId equals (Guid?)goal.Id into goals
			from goal in goals.DefaultIfEmpty()
			select new { Task = task, TaskList = taskList, Context = planningContext, Goal = goal };

		filtered = query.Status switch
		{
			PlannerTaskStatus.Open => filtered.Where(item => !item.Task.IsCompleted),
			PlannerTaskStatus.Completed => filtered.Where(item => item.Task.IsCompleted),
			_ => filtered,
		};

		if (query.ImportantOnly)
		{
			filtered = filtered.Where(item => item.Task.IsImportant);
		}

		if (query.TaskListId.HasValue)
		{
			filtered = filtered.Where(item => item.Task.TaskListId == query.TaskListId.Value);
		}

		if (query.ContextId.HasValue)
		{
			filtered = filtered.Where(item => item.TaskList.ContextId == query.ContextId.Value);
		}

		if (query.GoalId.HasValue)
		{
			filtered = filtered.Where(item => item.Task.GoalId == query.GoalId.Value);
		}

		if (query.Search is not null)
		{
			var search = query.Search.ToLowerInvariant();
			filtered = filtered.Where(item =>
				item.Task.Title.ToLower().Contains(search)
				|| (item.Task.Description != null && item.Task.Description.ToLower().Contains(search)));
		}

		var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
		var items = await filtered
			.OrderBy(item => item.Task.FocusAt == null)
			.ThenBy(item => item.Task.FocusAt)
			.ThenByDescending(item => item.Task.UpdatedAt)
			.ThenBy(item => item.Task.Id)
			.Skip(query.Offset)
			.Take(query.Limit)
			.Select(item => new PlannerTaskSummaryDto(
				item.Task.Id,
				item.Task.Title,
				item.Task.Description == null
					? null
					: item.Task.Description.Length <= PlannerReadDefaults.PreviewLength
						? item.Task.Description
						: item.Task.Description.Substring(0, PlannerReadDefaults.PreviewLength),
				item.Task.IsCompleted,
				item.Task.IsImportant,
				item.Task.DueAt,
				item.Task.FocusAt,
				item.Task.UpdatedAt,
				item.TaskList.Id,
				item.TaskList.Title,
				item.TaskList.ContextId,
				item.Context == null ? null : item.Context.Name,
				item.Task.GoalId,
				item.Goal == null ? null : item.Goal.Title))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return new PagedResult<PlannerTaskSummaryDto>(items, totalCount, query.Offset, query.Limit);
	}

	public async Task<PlannerTaskDetailDto?> GetTaskAsync(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		return await (
			from task in db.TaskItems.AsNoTracking()
			join taskList in db.TaskLists.AsNoTracking() on task.TaskListId equals taskList.Id
			join planningContext in db.PlanningContexts.AsNoTracking()
				on taskList.ContextId equals (Guid?)planningContext.Id into planningContexts
			from planningContext in planningContexts.DefaultIfEmpty()
			join goal in db.Goals.AsNoTracking()
				on task.GoalId equals (Guid?)goal.Id into goals
			from goal in goals.DefaultIfEmpty()
			where task.Id == id
			select new PlannerTaskDetailDto(
				task.Id,
				task.Title,
				task.Description,
				task.IsCompleted,
				task.IsImportant,
				task.CreatedAt,
				task.UpdatedAt,
				task.DueAt,
				task.CompletedAt,
				task.FocusAt,
				taskList.Id,
				taskList.Title,
				taskList.ContextId,
				planningContext == null ? null : planningContext.Name,
				task.GoalId,
				goal == null ? null : goal.Title))
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<PagedResult<PlannerGoalSummaryDto>> QueryGoalsAsync(
		PlannerGoalQuery query,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var filtered = db.Goals.AsNoTracking().AsQueryable();

		filtered = query.Status switch
		{
			PlannerGoalStatus.Active => filtered.Where(goal => goal.Status == GoalStatus.Active),
			PlannerGoalStatus.Achieved => filtered.Where(goal => goal.Status == GoalStatus.Achieved),
			PlannerGoalStatus.Dropped => filtered.Where(goal => goal.Status == GoalStatus.Dropped),
			_ => filtered,
		};

		if (query.Horizon.HasValue)
		{
			filtered = filtered.Where(goal => goal.Horizon == query.Horizon.Value);
		}

		if (query.Search is not null)
		{
			var search = query.Search.ToLowerInvariant();
			filtered = filtered.Where(goal =>
				goal.Title.ToLower().Contains(search)
				|| (goal.Description != null && goal.Description.ToLower().Contains(search)));
		}

		var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
		var items = await filtered
			.OrderByDescending(goal => goal.UpdatedAt)
			.ThenBy(goal => goal.Id)
			.Skip(query.Offset)
			.Take(query.Limit)
			.Select(goal => new PlannerGoalSummaryDto(
				goal.Id,
				goal.Title,
				goal.Description == null
					? null
					: goal.Description.Length <= PlannerReadDefaults.PreviewLength
						? goal.Description
						: goal.Description.Substring(0, PlannerReadDefaults.PreviewLength),
				goal.Horizon,
				goal.TargetPeriod,
				goal.TargetDate,
				goal.Status,
				goal.Metric,
				goal.MetricCurrent,
				goal.UpdatedAt))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return new PagedResult<PlannerGoalSummaryDto>(items, totalCount, query.Offset, query.Limit);
	}

	public async Task<PlannerGoalDetailDto?> GetGoalAsync(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		return await db.Goals
			.AsNoTracking()
			.Where(goal => goal.Id == id)
			.Select(goal => new PlannerGoalDetailDto(
				goal.Id,
				goal.Title,
				goal.Description,
				goal.Horizon,
				goal.TargetPeriod,
				goal.TargetDate,
				goal.Status,
				goal.Metric,
				goal.MetricCurrent,
				goal.CreatedAt,
				goal.UpdatedAt))
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<PagedResult<PlannerNoteSummaryDto>> QueryNotesAsync(
		PlannerNoteQuery query,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var filtered =
			from note in db.NoteItems.AsNoTracking()
			join noteList in db.NoteLists.AsNoTracking() on note.NoteListId equals noteList.Id
			join planningContext in db.PlanningContexts.AsNoTracking()
				on noteList.ContextId equals (Guid?)planningContext.Id into planningContexts
			from planningContext in planningContexts.DefaultIfEmpty()
			join goal in db.Goals.AsNoTracking()
				on note.GoalId equals (Guid?)goal.Id into goals
			from goal in goals.DefaultIfEmpty()
			select new { Note = note, NoteList = noteList, Context = planningContext, Goal = goal };

		if (query.PinnedOnly)
		{
			filtered = filtered.Where(item => item.Note.IsPinned);
		}

		if (query.NoteListId.HasValue)
		{
			filtered = filtered.Where(item => item.Note.NoteListId == query.NoteListId.Value);
		}

		if (query.ContextId.HasValue)
		{
			filtered = filtered.Where(item => item.NoteList.ContextId == query.ContextId.Value);
		}

		if (query.GoalId.HasValue)
		{
			filtered = filtered.Where(item => item.Note.GoalId == query.GoalId.Value);
		}

		if (query.Search is not null)
		{
			var search = query.Search.ToLowerInvariant();
			filtered = filtered.Where(item =>
				item.Note.Title.ToLower().Contains(search)
				|| (item.Note.Content != null && item.Note.Content.ToLower().Contains(search)));
		}

		var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
		var items = await filtered
			.OrderByDescending(item => item.Note.IsPinned)
			.ThenByDescending(item => item.Note.UpdatedAt)
			.ThenBy(item => item.Note.Id)
			.Skip(query.Offset)
			.Take(query.Limit)
			.Select(item => new PlannerNoteSummaryDto(
				item.Note.Id,
				item.Note.Title,
				item.Note.Content == null
					? null
					: item.Note.Content.Length <= PlannerReadDefaults.PreviewLength
						? item.Note.Content
						: item.Note.Content.Substring(0, PlannerReadDefaults.PreviewLength),
				item.Note.IsPinned,
				item.Note.UpdatedAt,
				item.NoteList.Id,
				item.NoteList.Title,
				item.NoteList.ContextId,
				item.Context == null ? null : item.Context.Name,
				item.Note.GoalId,
				item.Goal == null ? null : item.Goal.Title))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return new PagedResult<PlannerNoteSummaryDto>(items, totalCount, query.Offset, query.Limit);
	}

	public async Task<PlannerNoteDetailDto?> GetNoteAsync(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		return await (
			from note in db.NoteItems.AsNoTracking()
			join noteList in db.NoteLists.AsNoTracking() on note.NoteListId equals noteList.Id
			join planningContext in db.PlanningContexts.AsNoTracking()
				on noteList.ContextId equals (Guid?)planningContext.Id into planningContexts
			from planningContext in planningContexts.DefaultIfEmpty()
			join goal in db.Goals.AsNoTracking()
				on note.GoalId equals (Guid?)goal.Id into goals
			from goal in goals.DefaultIfEmpty()
			where note.Id == id
			select new PlannerNoteDetailDto(
				note.Id,
				note.Title,
				note.Content,
				note.IsPinned,
				note.CreatedAt,
				note.UpdatedAt,
				noteList.Id,
				noteList.Title,
				noteList.ContextId,
				planningContext == null ? null : planningContext.Name,
				note.GoalId,
				goal == null ? null : goal.Title))
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<PlannerFilterOptionsDto> GetFilterOptionsAsync(
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbContextFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var taskLists = await db.TaskLists
			.AsNoTracking()
			.OrderBy(list => list.Title)
			.ThenBy(list => list.Id)
			.Select(list => new PlannerListOptionDto(list.Id, list.Title, list.Color, list.IsArchived))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var noteLists = await db.NoteLists
			.AsNoTracking()
			.OrderBy(list => list.Title)
			.ThenBy(list => list.Id)
			.Select(list => new PlannerListOptionDto(list.Id, list.Title, list.Color, list.IsArchived))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var contexts = await db.PlanningContexts
			.AsNoTracking()
			.OrderBy(context => context.Name)
			.ThenBy(context => context.Id)
			.Select(context => new PlannerContextOptionDto(context.Id, context.Name, context.Color, context.Status))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var goals = await db.Goals
			.AsNoTracking()
			.OrderBy(goal => goal.Title)
			.ThenBy(goal => goal.Id)
			.Select(goal => new PlannerGoalOptionDto(goal.Id, goal.Title, goal.Status))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return new PlannerFilterOptionsDto(taskLists, noteLists, contexts, goals);
	}
}
