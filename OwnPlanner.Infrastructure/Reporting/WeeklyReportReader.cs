using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Reporting;

public sealed class WeeklyReportReader(
	IPlannerDbContextFactory dbContextFactory,
	TimeProvider timeProvider) : IWeeklyReportReader
{
	private const int PreviewLength = 200;
	private const string MissingContextName = "Unassigned or missing context";

	public async Task<WeeklyReport> GetAsync(
		WeeklyReportOptions options,
		CancellationToken cancellationToken = default)
	{
		options.Validate();
		var asOfUtc = timeProvider.GetUtcNow().UtcDateTime;
		var startDate = options.StartDate ?? DateOnly.FromDateTime(asOfUtc);
		var endDate = startDate.AddDays(7);
		var windowStartUtc = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var windowEndUtc = endDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

		await using var db = await dbContextFactory.CreateAsync(cancellationToken);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var lists = await db.TaskLists.AsNoTracking()
			.Select(list => new ListRow(list.Id, list.ContextId, list.IsArchived))
			.ToListAsync(cancellationToken);
		var allListIds = lists.Select(list => list.Id).ToHashSet();
		var activeLists = lists.Where(list => !list.IsArchived).ToList();
		var activeListIds = activeLists.Select(list => list.Id).ToHashSet();
		var contexts = await db.PlanningContexts.AsNoTracking()
			.Where(context => context.Status != ContextStatus.Archived)
			.Select(context => new ContextRow(context.Id, context.Name))
			.ToListAsync(cancellationToken);
		var goals = await db.Goals.AsNoTracking()
			.Where(goal => goal.Status == GoalStatus.Active)
			.Select(goal => new GoalRow(goal.Id, goal.Title))
			.ToListAsync(cancellationToken);
		var tasks = await db.TaskItems.AsNoTracking()
			.Where(task => task.TrashedAt == null)
			.Where(task => !task.IsCompleted && (activeListIds.Contains(task.TaskListId) || !allListIds.Contains(task.TaskListId)))
			.Select(task => new TaskRow(task.Id, task.Title, task.IsImportant, task.DueAt, task.FocusAt, task.TaskListId, task.GoalId))
			.ToListAsync(cancellationToken);

		var listContexts = activeLists.ToDictionary(list => list.Id, list => list.ContextId);
		var contextNames = contexts.ToDictionary(context => context.Id, context => context.Name);
		var orderedTasks = OrderTasks(tasks, asOfUtc).ToList();
		var windowTasks = orderedTasks.Where(task => InWindow(task.FocusAt, windowStartUtc, windowEndUtc) || InWindow(task.DueAt, windowStartUtc, windowEndUtc)).ToList();
		var overdueNotFocused = orderedTasks.Where(task => IsOverdue(task, asOfUtc) && !InWindow(task.FocusAt, windowStartUtc, windowEndUtc)).ToList();
		var importantWithoutFocus = orderedTasks.Where(task => task.IsImportant && task.FocusAt is null).ToList();
		var sampleIds = SelectSampleIds(startDate, options.TaskSampleLimit, windowTasks, overdueNotFocused, importantWithoutFocus, orderedTasks, goals, listContexts);
		var previews = await LoadPreviewsAsync(db, sampleIds, cancellationToken);

		var days = Enumerable.Range(0, 7).Select(offset =>
		{
			var date = startDate.AddDays(offset);
			var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			var focused = orderedTasks.Where(task => InWindow(task.FocusAt, dayStart, dayEnd)).ToList();
			var due = orderedTasks.Where(task => InWindow(task.DueAt, dayStart, dayEnd)).ToList();
			var distinctCount = focused.Select(task => task.Id).Union(due.Select(task => task.Id)).Count();
			return new WeeklyDaySummary(
				date, focused.Count, due.Count, distinctCount, distinctCount >= options.OverloadedDayThreshold,
				focused.Take(options.TaskSampleLimit).Select(task => ToSample(task, listContexts, previews)).ToList(),
				due.Take(options.TaskSampleLimit).Select(task => ToSample(task, listContexts, previews)).ToList());
		}).ToList();

		var contextGroups = orderedTasks.GroupBy(task => ResolveContext(task, listContexts, contextNames))
			.OrderBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(group => group.Key.Id)
			.Select(group => BuildContextSummary(group.Key, group.ToList(), windowStartUtc, windowEndUtc, asOfUtc, listContexts, previews, options.TaskSampleLimit))
			.ToList();
		var goalGroups = orderedTasks.Where(task => task.GoalId.HasValue).GroupBy(task => task.GoalId!.Value)
			.ToDictionary(group => group.Key, group => group.ToList());
		var goalSummaries = goals.OrderBy(goal => goal.Title, StringComparer.OrdinalIgnoreCase).ThenBy(goal => goal.Id)
			.Select(goal => BuildGoalSummary(goal, goalGroups.GetValueOrDefault(goal.Id) ?? [], windowStartUtc, windowEndUtc, asOfUtc, listContexts, previews, options.TaskSampleLimit))
			.ToList();

		return new WeeklyReport(
			asOfUtc, startDate, endDate, "UTC", "[windowStartDate, windowEndExclusiveDate)", options.OverloadedDayThreshold,
			new WeeklyOverallTotals(
				orderedTasks.Count(task => InWindow(task.FocusAt, windowStartUtc, windowEndUtc)),
				orderedTasks.Count(task => InWindow(task.DueAt, windowStartUtc, windowEndUtc)),
				windowTasks.Count,
				orderedTasks.Count(task => IsOverdue(task, asOfUtc)),
				orderedTasks.Count(task => task.IsImportant),
				orderedTasks.Count(task => task.FocusAt is null && task.DueAt is null)),
			days, contextGroups, goalSummaries,
			new WeeklyPlanningSignals(
				days.Where(day => day.IsOverloaded).Select(day => new WeeklyOverloadedDay(day.Date, day.DistinctTaskCount)).ToList(),
				goalSummaries.Where(goal => goal.FocusedInsideWindowCount == 0).Select(goal => new StrategicEntityReference(goal.Id, goal.Title)).ToList(),
				overdueNotFocused.Take(options.TaskSampleLimit).Select(task => ToSample(task, listContexts, previews)).ToList(), overdueNotFocused.Count,
				importantWithoutFocus.Take(options.TaskSampleLimit).Select(task => ToSample(task, listContexts, previews)).ToList(), importantWithoutFocus.Count));
	}

	private static WeeklyContextSummary BuildContextSummary(ContextKey key, IReadOnlyList<TaskRow> tasks, DateTime start, DateTime end, DateTime asOfUtc, IReadOnlyDictionary<Guid, Guid?> contexts, IReadOnlyDictionary<Guid, PreviewRow> previews, int limit)
	{
		var windowTasks = tasks.Where(task => InWindow(task.FocusAt, start, end) || InWindow(task.DueAt, start, end)).ToList();
		return new WeeklyContextSummary(key.Id, key.Name, key.Id is null, windowTasks.Count,
			tasks.Count(task => InWindow(task.FocusAt, start, end)), tasks.Count(task => InWindow(task.DueAt, start, end)),
			tasks.Count(task => task.IsImportant), tasks.Count(task => IsOverdue(task, asOfUtc)),
			windowTasks.Take(limit).Select(task => ToSample(task, contexts, previews)).ToList());
	}

	private static WeeklyGoalSummary BuildGoalSummary(GoalRow goal, IReadOnlyList<TaskRow> tasks, DateTime start, DateTime end, DateTime asOfUtc, IReadOnlyDictionary<Guid, Guid?> contexts, IReadOnlyDictionary<Guid, PreviewRow> previews, int limit)
	{
		var windowTasks = tasks.Where(task => InWindow(task.FocusAt, start, end) || InWindow(task.DueAt, start, end)).ToList();
		return new WeeklyGoalSummary(goal.Id, goal.Title, windowTasks.Count,
			tasks.Count(task => InWindow(task.FocusAt, start, end)), tasks.Count(task => InWindow(task.DueAt, start, end)),
			tasks.Count(task => task.IsImportant), tasks.Count(task => IsOverdue(task, asOfUtc)),
			windowTasks.Take(limit).Select(task => ToSample(task, contexts, previews)).ToList());
	}

	private static HashSet<Guid> SelectSampleIds(DateOnly startDate, int limit, IReadOnlyList<TaskRow> windowTasks, IReadOnlyList<TaskRow> overdue, IReadOnlyList<TaskRow> important, IReadOnlyList<TaskRow> allTasks, IReadOnlyList<GoalRow> goals, IReadOnlyDictionary<Guid, Guid?> contexts)
	{
		if (limit == 0) return [];
		var ids = new HashSet<Guid>(windowTasks.Take(limit).Select(task => task.Id));
		ids.UnionWith(overdue.Take(limit).Select(task => task.Id));
		ids.UnionWith(important.Take(limit).Select(task => task.Id));
		foreach (var offset in Enumerable.Range(0, 7))
		{
			var start = startDate.AddDays(offset).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			var end = startDate.AddDays(offset + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
			ids.UnionWith(allTasks.Where(task => InWindow(task.FocusAt, start, end)).Take(limit).Select(task => task.Id));
			ids.UnionWith(allTasks.Where(task => InWindow(task.DueAt, start, end)).Take(limit).Select(task => task.Id));
		}
		foreach (var group in windowTasks.GroupBy(task => contexts.GetValueOrDefault(task.TaskListId))) ids.UnionWith(group.Take(limit).Select(task => task.Id));
		foreach (var goal in goals) ids.UnionWith(windowTasks.Where(task => task.GoalId == goal.Id).Take(limit).Select(task => task.Id));
		return ids;
	}

	private static async Task<IReadOnlyDictionary<Guid, PreviewRow>> LoadPreviewsAsync(AppDbContext db, HashSet<Guid> ids, CancellationToken cancellationToken)
	{
		if (ids.Count == 0) return new Dictionary<Guid, PreviewRow>();
		return await db.TaskItems.AsNoTracking().Where(task => task.TrashedAt == null && ids.Contains(task.Id))
			.Select(task => new PreviewRow(task.Id,
				task.Description == null ? null : task.Description.Substring(0, Math.Min(task.Description.Length, PreviewLength)),
				task.Description != null && task.Description.Length > PreviewLength))
			.ToDictionaryAsync(row => row.Id, cancellationToken);
	}

	private static IOrderedEnumerable<TaskRow> OrderTasks(IEnumerable<TaskRow> tasks, DateTime asOfUtc) => tasks
		.OrderByDescending(task => IsOverdue(task, asOfUtc)).ThenByDescending(task => task.IsImportant)
		.ThenBy(task => task.DueAt ?? DateTime.MaxValue).ThenBy(task => task.FocusAt ?? DateTime.MaxValue).ThenBy(task => task.Id);
	private static bool IsOverdue(TaskRow task, DateTime asOfUtc) => task.DueAt.HasValue && task.DueAt.Value < asOfUtc;
	private static bool InWindow(DateTime? value, DateTime start, DateTime end) => value.HasValue && value.Value >= start && value.Value < end;
	private static ContextKey ResolveContext(TaskRow task, IReadOnlyDictionary<Guid, Guid?> listContexts, IReadOnlyDictionary<Guid, string> names)
	{
		var id = listContexts.GetValueOrDefault(task.TaskListId);
		return id.HasValue && names.TryGetValue(id.Value, out var name) ? new ContextKey(id, name) : new ContextKey(null, MissingContextName);
	}
	private static StrategicTaskSample ToSample(TaskRow task, IReadOnlyDictionary<Guid, Guid?> contexts, IReadOnlyDictionary<Guid, PreviewRow> previews)
	{
		var preview = previews.GetValueOrDefault(task.Id) ?? new PreviewRow(task.Id, null, false);
		return new StrategicTaskSample(task.Id, task.Title, preview.Value, preview.Truncated, task.IsImportant, task.DueAt, task.FocusAt, task.TaskListId, contexts.GetValueOrDefault(task.TaskListId), task.GoalId);
	}

	private sealed record ListRow(Guid Id, Guid? ContextId, bool IsArchived);
	private sealed record ContextRow(Guid Id, string Name);
	private sealed record GoalRow(Guid Id, string Title);
	private sealed record TaskRow(Guid Id, string Title, bool IsImportant, DateTime? DueAt, DateTime? FocusAt, Guid TaskListId, Guid? GoalId);
	private sealed record PreviewRow(Guid Id, string? Value, bool Truncated);
	private sealed record ContextKey(Guid? Id, string Name);
}
