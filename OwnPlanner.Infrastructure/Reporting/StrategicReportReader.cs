using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Reporting;

public sealed class StrategicReportReader(
	IPlannerDbContextFactory dbContextFactory,
	TimeProvider timeProvider) : IStrategicReportReader
{
	private const int PreviewLength = 200;

	public async Task<StrategicReport> GetAsync(
		StrategicReportOptions options,
		CancellationToken cancellationToken = default)
	{
		options.Validate();
		var asOfUtc = timeProvider.GetUtcNow().UtcDateTime;
		await using var db = await dbContextFactory.CreateAsync(cancellationToken);

		var contexts = await db.PlanningContexts.AsNoTracking()
			.Where(context => context.Status != ContextStatus.Archived)
			.Select(context => new ContextRow(context.Id, context.Name, context.Type, context.Status))
			.ToListAsync(cancellationToken);
		var goals = await db.Goals.AsNoTracking()
			.Where(goal => goal.Status == GoalStatus.Active)
			.Select(goal => new GoalRow(goal.Id, goal.Title, goal.Horizon, goal.TargetPeriod, goal.TargetDate, goal.Metric, goal.MetricCurrent))
			.ToListAsync(cancellationToken);
		var taskLists = await db.TaskLists.AsNoTracking()
			.Where(list => !list.IsArchived)
			.Select(list => new ListRow(list.Id, list.ContextId))
			.ToListAsync(cancellationToken);
		var noteLists = await db.NoteLists.AsNoTracking()
			.Where(list => !list.IsArchived)
			.Select(list => new ListRow(list.Id, list.ContextId))
			.ToListAsync(cancellationToken);

		var activeTaskListIds = taskLists.Select(list => list.Id).ToHashSet();
		var activeNoteListIds = noteLists.Select(list => list.Id).ToHashSet();
		var tasks = await db.TaskItems.AsNoTracking()
			.Where(task => !task.IsCompleted && activeTaskListIds.Contains(task.TaskListId))
			.Select(task => new TaskRow(task.Id, task.Title, task.IsImportant, task.DueAt, task.FocusAt, task.TaskListId, task.GoalId))
			.ToListAsync(cancellationToken);
		var notes = await db.NoteItems.AsNoTracking()
			.Where(note => activeNoteListIds.Contains(note.NoteListId))
			.Select(note => new NoteRow(note.Id, note.Title, note.IsPinned, note.UpdatedAt, note.NoteListId, note.GoalId))
			.ToListAsync(cancellationToken);

		var taskListContexts = taskLists.ToDictionary(list => list.Id, list => list.ContextId);
		var noteListContexts = noteLists.ToDictionary(list => list.Id, list => list.ContextId);
		var tasksWithoutGoal = tasks.Where(task => task.GoalId is null).ToList();
		var taskSampleIds = SelectTaskSampleIds(contexts, goals, tasks, tasksWithoutGoal, taskListContexts, asOfUtc, options.TaskSampleLimit);
		var noteSampleIds = SelectNoteSampleIds(contexts, goals, notes, noteListContexts, options.NoteSampleLimit);
		var taskPreviews = await LoadTaskPreviewsAsync(db, taskSampleIds, cancellationToken);
		var notePreviews = await LoadNotePreviewsAsync(db, noteSampleIds, cancellationToken);
		var contextSummaries = contexts
			.OrderBy(context => context.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(context => context.Id)
			.Select(context => BuildContextSummary(context, taskLists, noteLists, tasks, notes, taskListContexts, noteListContexts, taskPreviews, notePreviews, asOfUtc, options))
			.ToList();
		var goalSummaries = goals
			.OrderBy(goal => goal.Title, StringComparer.OrdinalIgnoreCase)
			.ThenBy(goal => goal.Id)
			.Select(goal => BuildGoalSummary(goal, tasks, notes, taskListContexts, noteListContexts, taskPreviews, notePreviews, asOfUtc, options))
			.ToList();

		return new StrategicReport(
			asOfUtc,
			new StrategicOverallTotals(
				contexts.Count,
				goals.Count,
				taskLists.Count,
				noteLists.Count,
				tasks.Count,
				tasks.Count(task => task.IsImportant),
				tasks.Count(task => IsOverdue(task, asOfUtc)),
				notes.Count),
			contextSummaries,
			goalSummaries,
			new StrategicStructuralSignals(
				goals.Where(goal => tasks.All(task => task.GoalId != goal.Id)).Select(goal => new StrategicEntityReference(goal.Id, goal.Title)).ToList(),
				contexts.Where(context => tasks.All(task => ContextId(task, taskListContexts) != context.Id)).Select(context => new StrategicEntityReference(context.Id, context.Name)).ToList(),
				OrderTasks(tasksWithoutGoal, asOfUtc).Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(),
				tasksWithoutGoal.Count,
				contexts.Where(context => taskLists.All(list => list.ContextId != context.Id) && noteLists.All(list => list.ContextId != context.Id)).Select(context => new StrategicEntityReference(context.Id, context.Name)).ToList()));
	}

	private static StrategicContextSummary BuildContextSummary(
		ContextRow context,
		IReadOnlyList<ListRow> taskLists,
		IReadOnlyList<ListRow> noteLists,
		IReadOnlyList<TaskRow> tasks,
		IReadOnlyList<NoteRow> notes,
		IReadOnlyDictionary<Guid, Guid?> taskListContexts,
		IReadOnlyDictionary<Guid, Guid?> noteListContexts,
		IReadOnlyDictionary<Guid, PreviewRow> taskPreviews,
		IReadOnlyDictionary<Guid, PreviewRow> notePreviews,
		DateTime asOfUtc,
		StrategicReportOptions options)
	{
		var contextTasks = tasks.Where(task => ContextId(task, taskListContexts) == context.Id).ToList();
		var contextNotes = notes.Where(note => ContextId(note, noteListContexts) == context.Id).ToList();
		return new StrategicContextSummary(
			context.Id, context.Name, context.Type, context.Status,
			taskLists.Count(list => list.ContextId == context.Id),
			noteLists.Count(list => list.ContextId == context.Id),
			contextTasks.Count,
			contextTasks.Count(task => task.IsImportant),
			contextTasks.Count(task => IsOverdue(task, asOfUtc)),
			contextTasks.Count(task => task.GoalId is not null),
			contextNotes.Count,
			OrderTasks(contextTasks, asOfUtc).Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(),
			OrderNotes(contextNotes).Take(options.NoteSampleLimit).Select(note => ToSample(note, noteListContexts, notePreviews)).ToList());
	}

	private static StrategicGoalSummary BuildGoalSummary(
		GoalRow goal,
		IReadOnlyList<TaskRow> tasks,
		IReadOnlyList<NoteRow> notes,
		IReadOnlyDictionary<Guid, Guid?> taskListContexts,
		IReadOnlyDictionary<Guid, Guid?> noteListContexts,
		IReadOnlyDictionary<Guid, PreviewRow> taskPreviews,
		IReadOnlyDictionary<Guid, PreviewRow> notePreviews,
		DateTime asOfUtc,
		StrategicReportOptions options)
	{
		var goalTasks = tasks.Where(task => task.GoalId == goal.Id).ToList();
		var goalNotes = notes.Where(note => note.GoalId == goal.Id).ToList();
		var contextIds = goalTasks.Select(task => ContextId(task, taskListContexts))
			.Concat(goalNotes.Select(note => ContextId(note, noteListContexts)))
			.Where(id => id.HasValue).Distinct().Count();
		return new StrategicGoalSummary(
			goal.Id, goal.Title, goal.Horizon, goal.TargetPeriod, goal.TargetDate, goal.Metric, goal.MetricCurrent,
			goalTasks.Count,
			goalTasks.Count(task => task.IsImportant),
			goalTasks.Count(task => IsOverdue(task, asOfUtc)),
			contextIds,
			goalTasks.Select(task => task.TaskListId).Distinct().Count(),
			goalNotes.Count,
			OrderTasks(goalTasks, asOfUtc).Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(),
			OrderNotes(goalNotes).Take(options.NoteSampleLimit).Select(note => ToSample(note, noteListContexts, notePreviews)).ToList());
	}

	private static HashSet<Guid> SelectTaskSampleIds(
		IReadOnlyList<ContextRow> contexts,
		IReadOnlyList<GoalRow> goals,
		IReadOnlyList<TaskRow> tasks,
		IReadOnlyList<TaskRow> tasksWithoutGoal,
		IReadOnlyDictionary<Guid, Guid?> taskListContexts,
		DateTime asOfUtc,
		int limit)
	{
		if (limit == 0)
			return [];

		var ids = new HashSet<Guid>();
		foreach (var context in contexts)
			ids.UnionWith(OrderTasks(tasks.Where(task => ContextId(task, taskListContexts) == context.Id), asOfUtc).Take(limit).Select(task => task.Id));
		foreach (var goal in goals)
			ids.UnionWith(OrderTasks(tasks.Where(task => task.GoalId == goal.Id), asOfUtc).Take(limit).Select(task => task.Id));
		ids.UnionWith(OrderTasks(tasksWithoutGoal, asOfUtc).Take(limit).Select(task => task.Id));
		return ids;
	}

	private static HashSet<Guid> SelectNoteSampleIds(
		IReadOnlyList<ContextRow> contexts,
		IReadOnlyList<GoalRow> goals,
		IReadOnlyList<NoteRow> notes,
		IReadOnlyDictionary<Guid, Guid?> noteListContexts,
		int limit)
	{
		if (limit == 0)
			return [];

		var ids = new HashSet<Guid>();
		foreach (var context in contexts)
			ids.UnionWith(OrderNotes(notes.Where(note => ContextId(note, noteListContexts) == context.Id)).Take(limit).Select(note => note.Id));
		foreach (var goal in goals)
			ids.UnionWith(OrderNotes(notes.Where(note => note.GoalId == goal.Id)).Take(limit).Select(note => note.Id));
		return ids;
	}

	private static async Task<IReadOnlyDictionary<Guid, PreviewRow>> LoadTaskPreviewsAsync(
		AppDbContext db,
		HashSet<Guid> sampleIds,
		CancellationToken cancellationToken)
	{
		if (sampleIds.Count == 0)
			return new Dictionary<Guid, PreviewRow>();

		return await db.TaskItems.AsNoTracking()
			.Where(task => sampleIds.Contains(task.Id))
			.Select(task => new PreviewRow(
				task.Id,
				task.Description == null ? null : task.Description.Substring(0, Math.Min(task.Description.Length, PreviewLength)),
				task.Description != null && task.Description.Length > PreviewLength))
			.ToDictionaryAsync(preview => preview.Id, cancellationToken);
	}

	private static async Task<IReadOnlyDictionary<Guid, PreviewRow>> LoadNotePreviewsAsync(
		AppDbContext db,
		HashSet<Guid> sampleIds,
		CancellationToken cancellationToken)
	{
		if (sampleIds.Count == 0)
			return new Dictionary<Guid, PreviewRow>();

		return await db.NoteItems.AsNoTracking()
			.Where(note => sampleIds.Contains(note.Id))
			.Select(note => new PreviewRow(
				note.Id,
				note.Content == null ? null : note.Content.Substring(0, Math.Min(note.Content.Length, PreviewLength)),
				note.Content != null && note.Content.Length > PreviewLength))
			.ToDictionaryAsync(preview => preview.Id, cancellationToken);
	}

	private static IOrderedEnumerable<TaskRow> OrderTasks(IEnumerable<TaskRow> tasks, DateTime asOfUtc) => tasks
		.OrderByDescending(task => IsOverdue(task, asOfUtc))
		.ThenByDescending(task => task.IsImportant)
		.ThenBy(task => task.DueAt ?? task.FocusAt ?? DateTime.MaxValue)
		.ThenBy(task => task.Id);

	private static IOrderedEnumerable<NoteRow> OrderNotes(IEnumerable<NoteRow> notes) => notes
		.OrderByDescending(note => note.IsPinned)
		.ThenByDescending(note => note.UpdatedAt)
		.ThenBy(note => note.Id);

	private static bool IsOverdue(TaskRow task, DateTime asOfUtc) => task.DueAt.HasValue && task.DueAt.Value < asOfUtc;
	private static Guid? ContextId(TaskRow task, IReadOnlyDictionary<Guid, Guid?> contexts) => contexts.GetValueOrDefault(task.TaskListId);
	private static Guid? ContextId(NoteRow note, IReadOnlyDictionary<Guid, Guid?> contexts) => contexts.GetValueOrDefault(note.NoteListId);

	private static StrategicTaskSample ToSample(
		TaskRow task,
		IReadOnlyDictionary<Guid, Guid?> contexts,
		IReadOnlyDictionary<Guid, PreviewRow> previews)
	{
		var preview = previews[task.Id];
		return new StrategicTaskSample(task.Id, task.Title, preview.Value, preview.Truncated, task.IsImportant, task.DueAt, task.FocusAt, task.TaskListId, ContextId(task, contexts), task.GoalId);
	}

	private static StrategicNoteSample ToSample(
		NoteRow note,
		IReadOnlyDictionary<Guid, Guid?> contexts,
		IReadOnlyDictionary<Guid, PreviewRow> previews)
	{
		var preview = previews[note.Id];
		return new StrategicNoteSample(note.Id, note.Title, preview.Value, preview.Truncated, note.IsPinned, note.UpdatedAt, note.NoteListId, ContextId(note, contexts), note.GoalId);
	}

	private sealed record ContextRow(Guid Id, string Name, ContextType Type, ContextStatus Status);
	private sealed record GoalRow(Guid Id, string Title, GoalHorizon Horizon, string? TargetPeriod, DateTime? TargetDate, string? Metric, string? MetricCurrent);
	private sealed record ListRow(Guid Id, Guid? ContextId);
	private sealed record TaskRow(Guid Id, string Title, bool IsImportant, DateTime? DueAt, DateTime? FocusAt, Guid TaskListId, Guid? GoalId);
	private sealed record NoteRow(Guid Id, string Title, bool IsPinned, DateTime UpdatedAt, Guid NoteListId, Guid? GoalId);
	private sealed record PreviewRow(Guid Id, string? Value, bool Truncated);
}
