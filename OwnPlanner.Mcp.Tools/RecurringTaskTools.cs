using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class RecurringTaskTools(IRecurringTaskManagement management, IPlanningWorkReader reader,
	ITaskOccurrenceHistory history, IRecurringTaskLifecycle lifecycle)
{
	[McpServerTool(Name = "recurring_task_create"), Description("Create ONE recurring series parent. Required missedPolicy: SkipExpiredPeriods or RetainEachObligation; ask the user if unspecified, never infer from the title. frequency Weekly requires weekdays (Sunday..Saturday names); Monthly requires monthDays (1..31, clamped/deduplicated per month). interval positive. startDate/endDate are YYYY-MM-DD, end inclusive; no daily frequency, deadlines or timed reminders. Weekly alignment uses shared calendar settings. Ordinary creation stays taskitem_create.")]
	public Task<object> Create(string title, Guid taskListId, string startDate, string frequency, string missedPolicy,
		int interval = 1, string[]? weekdays = null, int[]? monthDays = null, string? endDate = null,
		string? description = null, bool isImportant = false, Guid? goalId = null, CancellationToken ct = default) =>
		Run(async () => await management.CreateAsync(title, taskListId, Date(startDate), Rule(frequency, interval, weekdays, monthDays)!,
			Named<MissedOccurrencePolicy>(missedPolicy), OptionalDate(endDate), description, isImportant, goalId, ct));

	[McpServerTool(Name = "recurring_task_configure"), Description("Configure an existing active ORDINARY task as a series, preserving common fields. Required explicit missedPolicy and Weekly weekdays or Monthly monthDays. Dates YYYY-MM-DD. Cannot overwrite an existing series: use successor preview/apply to replace its schedule.")]
	public Task<object> Configure(Guid taskId, string startDate, string frequency, string missedPolicy,
		int interval = 1, string[]? weekdays = null, int[]? monthDays = null, string? endDate = null, CancellationToken ct = default) =>
		Run(async () => await management.ConfigureAsync(taskId, Date(startDate), Rule(frequency, interval, weekdays, monthDays)!,
			Named<MissedOccurrencePolicy>(missedPolicy), OptionalDate(endDate), ct));

	[McpServerTool(Name = "recurring_task_get", ReadOnly = true, Idempotent = true), Description("Read one series parent, immutable schedule and predecessor/successor IDs, including closed predecessors. No reconciliation or writes. taskitem list tools still return one stored row per series; use planning_work_query for occurrence details.")]
	public Task<object> Get(Guid taskId, CancellationToken ct = default) => Run(async () => await management.GetAsync(taskId, ct));

	[McpServerTool(Name = "planning_work_query", ReadOnly = true, Idempotent = true), Description("Read combined ordinary tasks and computed occurrences in bounded [startDate,endExclusiveDate), YYYY-MM-DD (max 3660 days). Independent current/next periods coexist. status Pending (default), Completed, Skipped, Cancelled or All; kind OrdinaryTask/Occurrence or omitted. ordinaryTasks FocusOrDeadlineInRange (default), FocusInRange, DeadlineInRange, AllOpen or None. includeRetainedBeforeStart adds ALL older unresolved retained obligations. Filters/counts/order apply before exact paging (limit 1..100). Counts distinguish stored ordinary/series and occurrence work. Each occurrence uses ORIGINAL (taskId,occurrenceDate), period and status. Source metadata/deadline appears once, never an occurrence deadline. No writes/expiry reconciliation. Refresh after actions and page while hasMore.")]
	public Task<object> Query(string startDate, string endExclusiveDate, bool includeRetainedBeforeStart = false,
		string status = "Pending", string? kind = null, string ordinaryTasks = "FocusOrDeadlineInRange",
		Guid? taskId = null, Guid? taskListId = null, Guid? goalId = null, Guid? contextId = null,
		bool importantOnly = false, string? search = null, int offset = 0, int limit = 25, CancellationToken ct = default) =>
		Run(async () => await reader.QueryAsync(new(new(Date(startDate), Date(endExclusiveDate)), includeRetainedBeforeStart,
			Named<OrdinaryTaskSelection>(ordinaryTasks), status == "All" ? null : Named<PlanningWorkStatus>(status),
			kind is null ? null : Named<PlanningWorkKind>(kind), taskId, taskListId, goalId, contextId, importantOnly, search, offset, limit), ct));

	[McpServerTool(Name = "task_occurrence_history", ReadOnly = true, Idempotent = true), Description("Read recorded facts including closed/predecessor series and dates outside shortened generation bounds. basis ScheduledDate requires startDate/endExclusiveDate YYYY-MM-DD; CompletionTime requires startUtc/endExclusiveUtc ISO instants with Z or explicit offset. Use exactly one basis (max 3660 days); limit 1..100. Optional taskId. Scheduled date and actual CompletedAt are independent, never infer one from the other. Returns exact counts, calendar and source metadata, no generated history or writes.")]
	public Task<object> History(string basis, string? startDate = null, string? endExclusiveDate = null,
		string? startUtc = null, string? endExclusiveUtc = null, Guid? taskId = null, int offset = 0, int limit = 25,
		CancellationToken ct = default) => Run(async () => await history.QueryAsync(new(Named<OccurrenceHistoryBasis>(basis),
			startDate is null && endExclusiveDate is null ? null : new(Date(startDate), Date(endExclusiveDate)),
			Instant(startUtc), Instant(endExclusiveUtc), taskId, offset, limit), ct));

	[McpServerTool(Name = "task_occurrence_get", ReadOnly = true, Idempotent = true), Description("Read one recorded fact by original taskId and occurrenceDate YYYY-MM-DD, including closed/predecessor history. A missing fact is not proof of completion or an eligible pending date; inspect planning_work_query. No writes.")]
	public Task<object> Fact(Guid taskId, string occurrenceDate, CancellationToken ct = default) => Run(async () =>
		(object?)await history.GetAsync(new(taskId, Date(occurrenceDate)), ct) ?? new { error = "Occurrence fact not found." });

	[McpServerTool(Name = "task_occurrence_complete", Idempotent = true), Description("Complete ONLY one explicitly identified (taskId,occurrenceDate YYYY-MM-DD), never its series. Can explicitly correct a recorded Skipped fact even on a closed/predecessor series. Optional completedAt must be an explicitly supplied actual ISO instant with Z/offset, not future. Ask about ambiguous local completion times; never derive actual time from the scheduled date. Omission records clock now. Identical retries preserve the FIRST CompletedAt. Refresh planning/history afterward; other occurrences remain independent.")]
	public Task<object> Complete(Guid taskId, string occurrenceDate, string? completedAt = null, CancellationToken ct = default) =>
		Run(async () => await lifecycle.CompleteOccurrenceAsync(new(taskId, Date(occurrenceDate)), Instant(completedAt), ct));

	[McpServerTool(Name = "task_occurrence_cancel", Idempotent = true), Description("Cancel ONLY one explicitly identified pending (taskId,occurrenceDate YYYY-MM-DD); keep the series and other obligations. Cannot overwrite completed/skipped facts. No CompletedAt is created. Refresh planning afterward.")]
	public Task<object> Cancel(Guid taskId, string occurrenceDate, CancellationToken ct = default) =>
		Run(async () => await lifecycle.CancelOccurrenceAsync(new(taskId, Date(occurrenceDate)), ct));

	[McpServerTool(Name = "recurring_task_successor_preview", ReadOnly = true, Idempotent = true), Description("Preview schedule replacement/resumption without writes. kind Replace for an open series, Resume for a completed chain tip; startDate YYYY-MM-DD required. Omitted frequency/policy inherit; a new frequency requires its full day selection. A changed Weekly rule also requires explicit weekStart 0..6 from calendar_period_get, reused identically on apply/retry so preference edits cannot change it. endDate inclusive; clearEndDate removes the bound. Show proposed schedule and ALL affected original facts to the user. If conflicts exist ask for explicit MoveStartAfterFacts or KeepStartAsSeparateWork; NEVER infer equivalent work or transfer completion. PreviewToken alone authorizes no choice.")]
	public Task<object> Preview(Guid taskId, string kind, string startDate, string? frequency = null, int interval = 1,
		string[]? weekdays = null, int[]? monthDays = null, string? missedPolicy = null, string? endDate = null,
		bool clearEndDate = false, int? weekStart = null, CancellationToken ct = default) => Run(async () => await management.PreviewAsync(taskId,
			Named<RecurringSuccessorKind>(kind), Date(startDate), Rule(frequency, interval, weekdays, monthDays, weekStart),
			missedPolicy is null ? null : Named<MissedOccurrencePolicy>(missedPolicy), OptionalDate(endDate), clearEndDate, ct));

	[McpServerTool(Name = "recurring_task_successor_apply", Idempotent = true), Description("Apply explicitly authorized replacement/resumption with the fresh previewToken and IDENTICAL preview inputs. requestId is a new UUID reused for identical retries. For affected facts resolution MUST be the user's explicit MoveStartAfterFacts or KeepStartAsSeparateWork decision after seeing facts/schedules. Atomically revalidates everything; stale/error means refresh and renew decision, never guess/retry a changed choice. Preserves original identities/timestamps and creates one linked successor, no copied deadline/focus or automatic completion transfer. Resume never reopens the predecessor or creates obligations during the break.")]
	public Task<object> Apply(Guid taskId, Guid requestId, string previewToken, string kind, string startDate,
		string? frequency = null, int interval = 1, string[]? weekdays = null, int[]? monthDays = null,
		string? missedPolicy = null, string? endDate = null, bool clearEndDate = false, string? resolution = null, int? weekStart = null,
		CancellationToken ct = default) => Run(async () => await management.ApplyAsync(taskId, requestId, previewToken,
			Named<RecurringSuccessorKind>(kind), Date(startDate), Rule(frequency, interval, weekdays, monthDays, weekStart),
			missedPolicy is null ? null : Named<MissedOccurrencePolicy>(missedPolicy), OptionalDate(endDate), clearEndDate,
			resolution is null ? null : Named<SuccessorConflictResolution>(resolution), ct));

	[McpServerTool(Name = "recurring_task_end", Idempotent = true), Description("Explicitly END THE WHOLE SERIES only when the user asks to stop future recurrence. Administrative parent closure adds no completed work; preserve all facts. For one performed occurrence use task_occurrence_complete instead. Resume later via successor preview/apply kind Resume with a user-selected start.")]
	public Task<object> End(Guid taskId, CancellationToken ct = default) => Run(async () =>
	{
		await lifecycle.EndSeriesAsync(taskId, ct);
		return new { success = true, taskId, seriesEnded = true };
	});

	private static RecurringRuleInput? Rule(string? frequency, int interval, string[]? weekdays, int[]? monthDays, int? weekStart = null)
	{
		if (frequency is not null) return new(Named<RecurrenceFrequency>(frequency), interval,
			weekdays?.Select(Named<DayOfWeek>).ToArray(), monthDays, weekStart);
		if (interval != 1 || weekdays is not null || monthDays is not null || weekStart is not null)
			throw new ArgumentException("A changed rule requires frequency and its complete day selection.");
		return null;
	}

	private static T Named<T>(string value) where T : struct, Enum =>
		Enum.GetNames<T>().Contains(value, StringComparer.Ordinal) ? Enum.Parse<T>(value) :
			throw new ArgumentException($"Expected {string.Join(", ", Enum.GetNames<T>())}.");
	private static DateOnly Date(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
		DateTimeStyles.None, out var result) ? result : throw new ArgumentException("Calendar dates must be YYYY-MM-DD.");
	private static DateOnly? OptionalDate(string? value) => value is null ? null : Date(value);
	private static DateTime? Instant(string? value)
	{
		if (value is null) return null;
		if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") ||
			!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			throw new ArgumentException("Actual instants must be ISO timestamps with explicit Z or UTC offset; clarify ambiguous local times.");
		return parsed.UtcDateTime;
	}
	private static async Task<object> Run(Func<Task<object>> action)
	{
		// Normalize once so direct and SDK transports share enum/null/date representations.
		try { return JsonSerializer.SerializeToElement(await action(), TaskToolSerialization.Options); }
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
		{ return new { error = ex.Message }; }
	}
}
