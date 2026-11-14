using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Explicit rule intent. New weekly parents default to shared calendar alignment; changed
/// successor rules require a captured WeekStart so preview/apply/retries are independent of preference edits.</summary>
public sealed record RecurringRuleInput(RecurrenceFrequency Frequency, int Interval = 1,
	IReadOnlyList<DayOfWeek>? Weekdays = null, IReadOnlyList<int>? MonthDays = null, int? WeekStart = null);

/// <summary>Original series metadata and immutable generation schedule, without projected task identities.</summary>
public sealed record RecurringTaskDetails(TaskItemDto Task, RecurrenceSchedule Schedule,
	Guid? PredecessorTaskId, Guid? SuccessorTaskId);

/// <summary>Host-bound creation/configuration and schedule intent mapping shared across transports.</summary>
public interface IRecurringTaskManagement
{
	/// <summary>Inserts exactly one configured parent. Policy is required; archived/missing lists reject creation.</summary>
	Task<RecurringTaskDetails> CreateAsync(string title, Guid taskListId, DateOnly startDate,
		RecurringRuleInput rule, MissedOccurrencePolicy missedPolicy, DateOnly? endDate = null,
		string? description = null, bool isImportant = false, Guid? goalId = null, CancellationToken ct = default);
	/// <summary>Atomically configures an active ordinary task. Existing series require a successor instead.</summary>
	Task<RecurringTaskDetails> ConfigureAsync(Guid taskId, DateOnly startDate, RecurringRuleInput rule,
		MissedOccurrencePolicy missedPolicy, DateOnly? endDate = null, CancellationToken ct = default);
	/// <summary>Reads an available series, including closed predecessors, without reconciliation.</summary>
	Task<RecurringTaskDetails> GetAsync(Guid taskId, CancellationToken ct = default);
	/// <summary>Maps optional rule changes and previews without writing. A changed Weekly rule requires
	/// explicit WeekStart from calendar metadata, reused unchanged on apply and retries.</summary>
	Task<RecurringSuccessorPreview> PreviewAsync(Guid taskId, RecurringSuccessorKind kind, DateOnly startDate,
		RecurringRuleInput? rule = null, MissedOccurrencePolicy? missedPolicy = null, DateOnly? endDate = null,
		bool clearEndDate = false, CancellationToken ct = default);
	/// <summary>Revalidates explicit preview/choice through the atomic successor service; matching retries return the same result.</summary>
	Task<RecurringSuccessorResult> ApplyAsync(Guid taskId, Guid requestId, string previewToken,
		RecurringSuccessorKind kind, DateOnly startDate, RecurringRuleInput? rule = null,
		MissedOccurrencePolicy? missedPolicy = null, DateOnly? endDate = null, bool clearEndDate = false,
		SuccessorConflictResolution? resolution = null, CancellationToken ct = default);
}

public sealed class RecurringTaskManagement(ITaskItemRepository tasks, ITaskListRepository lists,
	IRecurringTaskStore store, IPlanningCalendar calendar, IRecurringTaskSuccessors successors) : IRecurringTaskManagement
{
	public async Task<RecurringTaskDetails> CreateAsync(string title, Guid taskListId, DateOnly startDate,
		RecurringRuleInput rule, MissedOccurrencePolicy missedPolicy, DateOnly? endDate = null,
		string? description = null, bool isImportant = false, Guid? goalId = null, CancellationToken ct = default)
	{
		var schedule = new RecurrenceSchedule(await BuildRuleAsync(rule, ct), startDate, endDate, missedPolicy);
		var list = await lists.GetAsync(taskListId, ct);
		if (list is null || list.IsArchived) throw new KeyNotFoundException("Available task list not found.");
		var task = new TaskItem(title, taskListId, description, isImportant: isImportant, goalId: goalId);
		task.ConfigureRecurrence(schedule);
		await tasks.AddAsync(task, ct);
		return Details(task);
	}

	public async Task<RecurringTaskDetails> ConfigureAsync(Guid taskId, DateOnly startDate, RecurringRuleInput rule,
		MissedOccurrencePolicy missedPolicy, DateOnly? endDate = null, CancellationToken ct = default)
	{
		var schedule = new RecurrenceSchedule(await BuildRuleAsync(rule, ct), startDate, endDate, missedPolicy);
		return await store.ExecuteAsync(taskId, (tx, _) =>
		{
			RequireAvailable(tx);
			tx.Task.ConfigureRecurrence(schedule);
			return Task.FromResult(Details(tx.Task));
		}, ct);
	}

	public Task<RecurringTaskDetails> GetAsync(Guid taskId, CancellationToken ct = default) =>
		store.ReadAsync(taskId, (read, _) =>
		{
			RequireAvailable(read);
			return Task.FromResult(Details(read.Task));
		}, ct);

	public async Task<RecurringSuccessorPreview> PreviewAsync(Guid taskId, RecurringSuccessorKind kind,
		DateOnly startDate, RecurringRuleInput? rule = null, MissedOccurrencePolicy? missedPolicy = null,
		DateOnly? endDate = null, bool clearEndDate = false, CancellationToken ct = default) =>
		await successors.PreviewAsync(taskId, await RequestAsync(kind, startDate, rule, missedPolicy, endDate, clearEndDate, ct), ct);

	public async Task<RecurringSuccessorResult> ApplyAsync(Guid taskId, Guid requestId, string previewToken,
		RecurringSuccessorKind kind, DateOnly startDate, RecurringRuleInput? rule = null,
		MissedOccurrencePolicy? missedPolicy = null, DateOnly? endDate = null, bool clearEndDate = false,
		SuccessorConflictResolution? resolution = null, CancellationToken ct = default) =>
		await successors.ApplyAsync(taskId, requestId, await RequestAsync(kind, startDate, rule, missedPolicy, endDate, clearEndDate, ct),
			previewToken, resolution, ct);

	private async Task<RecurringSuccessorRequest> RequestAsync(RecurringSuccessorKind kind, DateOnly startDate,
		RecurringRuleInput? rule, MissedOccurrencePolicy? policy, DateOnly? endDate, bool clearEndDate, CancellationToken ct) =>
		new(kind, startDate, rule is null ? null : await BuildSuccessorRuleAsync(rule, ct), policy, endDate, clearEndDate);

	private Task<RecurrenceRule> BuildSuccessorRuleAsync(RecurringRuleInput rule, CancellationToken ct)
	{
		// Explicit alignment makes preview/apply and successful retries independent of later preference edits.
		if (rule.Frequency == RecurrenceFrequency.Weekly && rule.WeekStart is null)
			throw new ArgumentException("A changed Weekly rule requires explicit weekStart (0..6) from calendar_period_get; reuse it on apply/retry.");
		return BuildRuleAsync(rule, ct);
	}

	private async Task<RecurrenceRule> BuildRuleAsync(RecurringRuleInput input, CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(input);
		return input.Frequency switch
		{
			RecurrenceFrequency.Weekly when input.MonthDays is null => RecurrenceRule.Weekly(input.Interval,
				input.Weekdays ?? [], (DayOfWeek)(input.WeekStart ?? (await calendar.ResolveAsync("today", ct)).WeekStart)),
			RecurrenceFrequency.Monthly when input.Weekdays is null && input.WeekStart is null => RecurrenceRule.Monthly(input.Interval, input.MonthDays ?? []),
			_ => throw new ArgumentException("Use Weekly with weekdays, or Monthly with monthDays, exclusively.")
		};
	}

	private static void RequireAvailable(IRecurringTaskSnapshot read)
	{
		if (!read.ListAvailable || read.Task.TrashedAt.HasValue) throw new KeyNotFoundException("Available task not found.");
	}

	private static RecurringTaskDetails Details(TaskItem task) => new(TaskItemService.Map(task),
		task.Recurrence ?? throw new InvalidOperationException("Task is not recurring."),
		task.RecurrencePredecessorTaskId, task.RecurrenceSuccessorTaskId);
}
