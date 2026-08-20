using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Reporting;

public sealed class ReflectionReportReader(
	IPlannerDbContextFactory dbContextFactory,
	TimeProvider timeProvider) : IReflectionReportReader
{
	private const int PreviewLength = 200;
	private const string MissingContextName = "Unassigned or missing context";
	private static readonly IReadOnlyList<string> Limitations = Array.AsReadOnly(
	[
		"Completed work uses the task's current persisted CompletedAt value; reopened tasks have no historical completion event.",
		"Goal status, task assignments, and note-list membership are current state, not historical transitions.",
		"Notes removed from Inbox cannot be reconstructed as prior Inbox activity."
	]);

	public async Task<ReflectionReport> GetAsync(
		ReflectionReportOptions options,
		CancellationToken cancellationToken = default)
	{
		options.Validate();
		var asOfUtc = timeProvider.GetUtcNow().UtcDateTime;
		var endUtc = options.EndAtUtc ?? asOfUtc;
		var startUtc = endUtc.AddDays(-options.PeriodDays);

		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var taskLists = await db.TaskLists.AsNoTracking()
			.Select(list => new ListRow(list.Id, list.ContextId, list.IsArchived))
			.ToListAsync(cancellationToken);
		var noteLists = await db.NoteLists.AsNoTracking()
			.Select(list => new ListRow(list.Id, list.ContextId, list.IsArchived))
			.ToListAsync(cancellationToken);
		var contexts = await db.PlanningContexts.AsNoTracking()
			.Where(context => context.Status != ContextStatus.Archived)
			.Select(context => new ContextRow(context.Id, context.Name))
			.ToListAsync(cancellationToken);
		var goals = await db.Goals.AsNoTracking()
			.Where(goal => goal.Status == GoalStatus.Active)
			.Select(goal => new GoalRow(goal.Id, goal.Title))
			.ToListAsync(cancellationToken);

		var activeTaskLists = taskLists.Where(list => !list.IsArchived).ToList();
		var allTaskListIds = taskLists.Select(list => list.Id).ToHashSet();
		var activeTaskListIds = activeTaskLists.Select(list => list.Id).ToHashSet();
		var tasks = await db.TaskItems.AsNoTracking()
			.Where(task =>
				(activeTaskListIds.Contains(task.TaskListId) || !allTaskListIds.Contains(task.TaskListId)) &&
				(!task.IsCompleted ||
				 (task.CreatedAt >= startUtc && task.CreatedAt < endUtc) ||
				 (task.CompletedAt.HasValue && task.CompletedAt.Value >= startUtc && task.CompletedAt.Value < endUtc)))
			.Select(task => new TaskRow(task.Id, task.Title, task.IsCompleted, task.IsImportant, task.CreatedAt, task.CompletedAt, task.DueAt, task.FocusAt, task.TaskListId, task.GoalId))
			.ToListAsync(cancellationToken);
		var activeNoteLists = noteLists.Where(list => !list.IsArchived).ToList();
		var allNoteListIds = noteLists.Select(list => list.Id).ToHashSet();
		var activeNoteListIds = activeNoteLists.Select(list => list.Id).ToHashSet();
		var notes = await db.NoteItems.AsNoTracking()
			.Where(note =>
				(activeNoteListIds.Contains(note.NoteListId) || !allNoteListIds.Contains(note.NoteListId)) &&
				(note.NoteListId == WellKnownIds.InboxNoteList ||
				 (note.CreatedAt >= startUtc && note.CreatedAt < endUtc) ||
				 (note.UpdatedAt >= startUtc && note.UpdatedAt < endUtc)))
			.Select(note => new NoteRow(note.Id, note.Title, note.IsPinned, note.CreatedAt, note.UpdatedAt, note.NoteListId, note.GoalId))
			.ToListAsync(cancellationToken);

		var contextNames = contexts.ToDictionary(context => context.Id, context => context.Name);
		var taskListContexts = activeTaskLists.ToDictionary(list => list.Id, list => KnownContextId(list.ContextId, contextNames));
		var noteListContexts = activeNoteLists.ToDictionary(list => list.Id, list => KnownContextId(list.ContextId, contextNames));
		var completed = OrderCompleted(tasks.Where(task => task.IsCompleted && InPeriod(task.CompletedAt, startUtc, endUtc))).ToList();
		var missedFocus = OrderUnresolved(tasks.Where(task => !task.IsCompleted && InPeriod(task.FocusAt, startUtc, endUtc)), asOfUtc).ToList();
		var incomplete = OrderUnresolved(tasks.Where(task => !task.IsCompleted), asOfUtc).ToList();
		var overdue = incomplete.Where(task => IsOverdue(task, asOfUtc)).ToList();
		var inbox = notes.Where(note => note.NoteListId == WellKnownIds.InboxNoteList && activeNoteListIds.Contains(note.NoteListId))
			.OrderByDescending(note => note.IsPinned).ThenByDescending(note => note.UpdatedAt).ThenBy(note => note.Id).ToList();

		var contextGroups = completed.Concat(missedFocus).GroupBy(task => ResolveContext(task, taskListContexts, contextNames))
			.OrderBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Key.Id).ToList();
		var tasksByGoal = tasks.Where(task => task.GoalId.HasValue).GroupBy(task => task.GoalId!.Value)
			.ToDictionary(group => group.Key, group => group.ToList());
		var sampleIds = SelectTaskSampleIds(contextGroups, goals, tasksByGoal, missedFocus, overdue, startUtc, endUtc, asOfUtc, options.TaskSampleLimit);
		var taskPreviews = await LoadTaskPreviewsAsync(db, sampleIds, cancellationToken);
		var inboxSampleIds = inbox.Take(options.NoteSampleLimit).Select(note => note.Id).ToHashSet();
		var notePreviews = await LoadNotePreviewsAsync(db, inboxSampleIds, cancellationToken);

		var contextSummaries = contextGroups.Select(group =>
		{
			var groupCompleted = OrderCompleted(group.Where(task => task.IsCompleted && InPeriod(task.CompletedAt, startUtc, endUtc))).ToList();
			var groupMissed = OrderUnresolved(group.Where(task => !task.IsCompleted && InPeriod(task.FocusAt, startUtc, endUtc)), asOfUtc).ToList();
			return new ReflectionContextSummary(
				group.Key.Id, group.Key.Name, group.Key.Id is null, groupCompleted.Count, groupMissed.Count,
				groupCompleted.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(),
				groupMissed.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList());
		}).ToList();
		var goalSummaries = goals.OrderBy(goal => goal.Title, StringComparer.OrdinalIgnoreCase).ThenBy(goal => goal.Id).Select(goal =>
		{
			var goalTasks = tasksByGoal.GetValueOrDefault(goal.Id) ?? [];
			var goalCompleted = OrderCompleted(goalTasks.Where(task => task.IsCompleted && InPeriod(task.CompletedAt, startUtc, endUtc))).ToList();
			var goalIncomplete = OrderUnresolved(goalTasks.Where(task => !task.IsCompleted), asOfUtc).ToList();
			var goalMissed = goalIncomplete.Where(task => InPeriod(task.FocusAt, startUtc, endUtc)).ToList();
			return new ReflectionGoalSummary(
				goal.Id, goal.Title, goalCompleted.Count, goalIncomplete.Count, goalIncomplete.Count(task => IsOverdue(task, asOfUtc)), goalMissed.Count,
				goalCompleted.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(),
				goalMissed.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList());
		}).ToList();

		return new ReflectionReport(
			asOfUtc, startUtc, endUtc, "UTC", "[periodStartUtc, periodEndExclusiveUtc)", Limitations,
			new ReflectionOverallTotals(
				completed.Count,
				tasks.Count(task => InPeriod(task.CreatedAt, startUtc, endUtc)),
				missedFocus.Count,
				overdue.Count,
				notes.Count(note => InPeriod(note.CreatedAt, startUtc, endUtc) || InPeriod(note.UpdatedAt, startUtc, endUtc)),
				inbox.Count),
			contextSummaries, goalSummaries,
			new ReflectionInboxSummary(
				WellKnownIds.InboxNoteList, inbox.Count,
				inbox.Take(options.NoteSampleLimit).Select(note => ToSample(note, noteListContexts, notePreviews)).ToList()),
			new ReflectionSignals(
				missedFocus.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(), missedFocus.Count,
				goalSummaries.Where(goal => goal.CompletedTaskCount == 0).Select(goal => new StrategicEntityReference(goal.Id, goal.Title)).ToList(),
				overdue.Take(options.TaskSampleLimit).Select(task => ToSample(task, taskListContexts, taskPreviews)).ToList(), overdue.Count,
				inbox.Count));
	}

	private static HashSet<Guid> SelectTaskSampleIds(
		IReadOnlyList<IGrouping<ContextKey, TaskRow>> contextGroups,
		IReadOnlyList<GoalRow> goals,
		IReadOnlyDictionary<Guid, List<TaskRow>> tasksByGoal,
		IReadOnlyList<TaskRow> missed,
		IReadOnlyList<TaskRow> overdue,
		DateTime startUtc,
		DateTime endUtc,
		DateTime asOfUtc,
		int limit)
	{
		if (limit == 0) return [];
		var ids = new HashSet<Guid>();
		ids.UnionWith(missed.Take(limit).Select(task => task.Id));
		ids.UnionWith(overdue.Take(limit).Select(task => task.Id));
		foreach (var group in contextGroups)
		{
			ids.UnionWith(OrderCompleted(group.Where(task => task.IsCompleted && InPeriod(task.CompletedAt, startUtc, endUtc))).Take(limit).Select(task => task.Id));
			ids.UnionWith(OrderUnresolved(group.Where(task => !task.IsCompleted && InPeriod(task.FocusAt, startUtc, endUtc)), asOfUtc).Take(limit).Select(task => task.Id));
		}
		foreach (var goal in goals)
		{
			var goalTasks = tasksByGoal.GetValueOrDefault(goal.Id) ?? [];
			ids.UnionWith(OrderCompleted(goalTasks.Where(task => task.IsCompleted && InPeriod(task.CompletedAt, startUtc, endUtc))).Take(limit).Select(task => task.Id));
			ids.UnionWith(OrderUnresolved(goalTasks.Where(task => !task.IsCompleted && InPeriod(task.FocusAt, startUtc, endUtc)), asOfUtc).Take(limit).Select(task => task.Id));
		}
		return ids;
	}

	private static async Task<IReadOnlyDictionary<Guid, PreviewRow>> LoadTaskPreviewsAsync(AppDbContext db, HashSet<Guid> ids, CancellationToken cancellationToken)
	{
		if (ids.Count == 0) return new Dictionary<Guid, PreviewRow>();
		return await db.TaskItems.AsNoTracking().Where(task => ids.Contains(task.Id))
			.Select(task => new PreviewRow(task.Id,
				task.Description == null ? null : task.Description.Substring(0, Math.Min(task.Description.Length, PreviewLength)),
				task.Description != null && task.Description.Length > PreviewLength))
			.ToDictionaryAsync(row => row.Id, cancellationToken);
	}

	private static async Task<IReadOnlyDictionary<Guid, PreviewRow>> LoadNotePreviewsAsync(AppDbContext db, HashSet<Guid> ids, CancellationToken cancellationToken)
	{
		if (ids.Count == 0) return new Dictionary<Guid, PreviewRow>();
		return await db.NoteItems.AsNoTracking().Where(note => ids.Contains(note.Id))
			.Select(note => new PreviewRow(note.Id,
				note.Content == null ? null : note.Content.Substring(0, Math.Min(note.Content.Length, PreviewLength)),
				note.Content != null && note.Content.Length > PreviewLength))
			.ToDictionaryAsync(row => row.Id, cancellationToken);
	}

	private static IOrderedEnumerable<TaskRow> OrderCompleted(IEnumerable<TaskRow> tasks) => tasks
		.OrderByDescending(task => task.CompletedAt).ThenByDescending(task => task.IsImportant)
		.ThenBy(task => task.DueAt ?? DateTime.MaxValue).ThenBy(task => task.FocusAt ?? DateTime.MaxValue).ThenBy(task => task.Id);
	private static IOrderedEnumerable<TaskRow> OrderUnresolved(IEnumerable<TaskRow> tasks, DateTime asOfUtc) => tasks
		.OrderByDescending(task => IsOverdue(task, asOfUtc)).ThenByDescending(task => task.IsImportant)
		.ThenBy(task => task.DueAt ?? DateTime.MaxValue).ThenBy(task => task.FocusAt ?? DateTime.MaxValue).ThenBy(task => task.Id);
	private static bool InPeriod(DateTime? value, DateTime startUtc, DateTime endUtc) => value.HasValue && value.Value >= startUtc && value.Value < endUtc;
	private static bool IsOverdue(TaskRow task, DateTime asOfUtc) => task.DueAt.HasValue && task.DueAt.Value < asOfUtc;
	private static Guid? KnownContextId(Guid? id, IReadOnlyDictionary<Guid, string> names) => id.HasValue && names.ContainsKey(id.Value) ? id : null;
	private static ContextKey ResolveContext(TaskRow task, IReadOnlyDictionary<Guid, Guid?> listContexts, IReadOnlyDictionary<Guid, string> names)
	{
		var id = listContexts.GetValueOrDefault(task.TaskListId);
		return id.HasValue && names.TryGetValue(id.Value, out var name) ? new ContextKey(id, name) : new ContextKey(null, MissingContextName);
	}
	private static ReflectionTaskSample ToSample(TaskRow task, IReadOnlyDictionary<Guid, Guid?> contexts, IReadOnlyDictionary<Guid, PreviewRow> previews)
	{
		var preview = previews.GetValueOrDefault(task.Id) ?? new PreviewRow(task.Id, null, false);
		return new ReflectionTaskSample(task.Id, task.Title, preview.Value, preview.Truncated, task.IsImportant, task.CreatedAt, task.CompletedAt, task.DueAt, task.FocusAt, task.TaskListId, contexts.GetValueOrDefault(task.TaskListId), task.GoalId);
	}
	private static StrategicNoteSample ToSample(NoteRow note, IReadOnlyDictionary<Guid, Guid?> contexts, IReadOnlyDictionary<Guid, PreviewRow> previews)
	{
		var preview = previews.GetValueOrDefault(note.Id) ?? new PreviewRow(note.Id, null, false);
		return new StrategicNoteSample(note.Id, note.Title, preview.Value, preview.Truncated, note.IsPinned, note.UpdatedAt, note.NoteListId, contexts.GetValueOrDefault(note.NoteListId), note.GoalId);
	}

	private sealed record ListRow(Guid Id, Guid? ContextId, bool IsArchived);
	private sealed record ContextRow(Guid Id, string Name);
	private sealed record GoalRow(Guid Id, string Title);
	private sealed record TaskRow(Guid Id, string Title, bool IsCompleted, bool IsImportant, DateTime CreatedAt, DateTime? CompletedAt, DateTime? DueAt, DateTime? FocusAt, Guid TaskListId, Guid? GoalId);
	private sealed record NoteRow(Guid Id, string Title, bool IsPinned, DateTime CreatedAt, DateTime UpdatedAt, Guid NoteListId, Guid? GoalId);
	private sealed record PreviewRow(Guid Id, string? Value, bool Truncated);
	private sealed record ContextKey(Guid? Id, string Name);
}
