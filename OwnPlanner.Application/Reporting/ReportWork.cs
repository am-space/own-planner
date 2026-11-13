using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Reporting;

/// <summary>Mixed work, separate from legacy stored-task counters. Scheduled selects calendar dates;
/// activity selects actual completion instants. Assignments and preferences are current state.</summary>
public sealed record ReportWork(PlanningPeriod Calendar, OccurrenceDateRange ScheduledRange,
	DateTime ActivityStartUtc, DateTime ActivityEndExclusiveUtc, ReportWorkSection Pending,
	ReportWorkSection Scheduled, ReportWorkSection CompletionActivity,
	IReadOnlyList<ReportWorkGroup> Goals, IReadOnlyList<ReportWorkGroup> Contexts,
	IReadOnlyList<ReportWorkDay> Days, IReadOnlyList<ReportWorkSource> Sources)
{
	public string SelectionSemantics => "Pending includes window work, unfinished current-period occurrences before the window, and the complete unresolved retained prefix. Scheduled selects original occurrence dates or ordinary focus/deadline dates inside ScheduledRange. CompletionActivity selects CompletedAt in [ActivityStartUtc, ActivityEndExclusiveUtc). Sections overlap; never sum them. Recurring-parent closure is not work; counts do not measure effort or capacity. Samples are ordered by selection date, task ID, occurrence date.";
}

public sealed record ReportWorkSection(PlanningWorkCounts Counts, int PendingCount, int PendingOccurrenceCount, int CompletedCount,
	int SkippedCount, int CancelledCount, int BeforeWindowCount, int SampleLimit,
	bool Truncated, IReadOnlyList<ReportWorkSample> Samples)
{
	public int TotalCount => Counts.WorkItemCount;
}

/// <summary>Occurrence identity is (TaskId, OccurrenceDate); ordinary identity is TaskId.
/// Dates are calendar values, never generated deadlines. Source metadata is deduplicated separately.</summary>
public sealed record ReportWorkSample(Guid TaskId, DateOnly? OccurrenceDate, DateOnly? SelectionDate,
	OccurrencePeriod? Period, PlanningWorkStatus Status, DateTime? CompletedAt,
	string Title, bool TitleTruncated);
public sealed record ReportWorkSource(Guid TaskId, Guid TaskListId, Guid? GoalId, Guid? ContextId,
	bool IsImportant, DateTime? FocusAt, DateTime? DueAt, bool IsRecurring);
public sealed record ReportWorkGroup(Guid? Id, ReportWorkSection Pending, ReportWorkSection Scheduled,
	ReportWorkSection CompletionActivity);
public sealed record ReportWorkDay(DateOnly Date, ReportWorkSection Pending, ReportWorkSection Scheduled,
	ReportWorkSection CompletionActivity);

/// <summary>Ordinary completion metadata supplied by a trusted reader. Series closure must never be supplied here.</summary>
public sealed record ReportOrdinaryCompletion(PlanningWorkSource Source, DateTime CompletedAt);

/// <summary>Aggregates the shared planning projection and independent recorded facts inside a caller's
/// snapshot. Exact counts never depend on bounded samples; no recurrence rules are implemented here.</summary>
public static class ReportWorkBuilder
{
	public static async Task<ReportWork> BuildAsync(IPlanningWorkSnapshot snapshot, PlanningPeriod calendar,
		OccurrenceDateRange range, DateTime activityStartUtc, DateTime activityEndUtc,
		IReadOnlyList<ReportOrdinaryCompletion> ordinaryCompletions, int sampleLimit,
		IRecurrenceCalculator calculator, CancellationToken ct = default, bool includeGroups = true)
	{
		RecurrenceReadLimits.ValidateRange(range);
		if (sampleLimit is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(sampleLimit));
		if (activityStartUtc.Kind != DateTimeKind.Utc || activityEndUtc.Kind != DateTimeKind.Utc || activityEndUtc <= activityStartUtc)
			throw new ArgumentException("Activity requires a positive half-open UTC range.");
		var pending = new Aggregate(sampleLimit, range.StartDate);
		var scheduled = new Aggregate(sampleLimit, range.StartDate);
		var activity = new Aggregate(sampleLimit, range.StartDate);
		var goals = new Dictionary<Guid, Group>();
		var contexts = new Dictionary<Guid, Group>();
		var days = new SortedDictionary<DateOnly, Group>();
		var sources = (await snapshot.GetSourcesAsync(true, ct)).ToDictionary(s => s.TaskId);
		var scheduledFacts = (await snapshot.GetScheduledFactsAsync(range, null, ct)).Where(f => sources.ContainsKey(f.Id.TaskId)).ToDictionary(f => f.Id);

		// Include still-actionable earlier dates of the current week/month as well as retained work.
		// Calendar period boundaries come from the same helper used by the shared projection.
		var currentStart = new[] { RecurrenceFrequency.Weekly, RecurrenceFrequency.Monthly }
			.Select(f => RecurrenceCalendar.PeriodContaining(f, calendar.Today, calendar).Dates)
			.Where(p => p.StartDate < range.EndExclusiveDate && p.EndExclusiveDate > range.StartDate)
			.Select(p => p.StartDate).Append(range.StartDate).Min();
		var ordinaryQuery = new PlanningWorkQuery(range);
		await PlanningWorkReader.VisitAsync(new(new(currentStart, range.EndExclusiveDate),
			IncludeRetainedBeforeStart: true, Status: null), calendar, snapshot, calculator, (item, source) =>
		{
			if (item.Kind == PlanningWorkKind.OrdinaryTask)
			{
				// Earlier occurrence dates must not make an out-of-window focus hide a selected deadline.
				var ordinary = PlanningWorkReader.ProjectOrdinary(source, ordinaryQuery, calendar);
				if (ordinary is null) return;
				item = ordinary;
			}
			var inside = item.WorkDate >= range.StartDate && item.WorkDate < range.EndExclusiveDate;
			if (item.Status == PlanningWorkStatus.Pending && (inside || item.Kind == PlanningWorkKind.Occurrence)) Add(0, item, source, item.WorkDate);
			// Recorded facts are added independently, including closed and shortened predecessors.
			if (inside && (item.OccurrenceDate is null || !scheduledFacts.ContainsKey(new(item.TaskId, item.OccurrenceDate.Value))))
				Add(1, item, source, item.WorkDate);
		}, ct);
		foreach (var fact in scheduledFacts.Values)
		{
			ct.ThrowIfCancellationRequested();
			var source = sources[fact.Id.TaskId];
			Add(1, FromFact(fact, source, calendar), source, fact.Id.OccurrenceDate);
		}
		foreach (var fact in await snapshot.GetCompletionFactsAsync(activityStartUtc, activityEndUtc, null, ct))
		{
			ct.ThrowIfCancellationRequested();
			if (!sources.TryGetValue(fact.Id.TaskId, out var source)) continue;
			Add(2, FromFact(fact, source, calendar), source, LocalDate(fact.CompletedAt!.Value));
		}
		foreach (var completion in ordinaryCompletions)
		{
			ct.ThrowIfCancellationRequested();
			var source = completion.Source;
			if (source.Schedule is not null) throw new ArgumentException("Series closure is not ordinary completed work.", nameof(ordinaryCompletions));
			var focus = source.FocusAt is { } f ? DateOnly.FromDateTime(f) : (DateOnly?)null;
			var due = source.DueAt is { } d ? LocalDate(d) : (DateOnly?)null;
			var date = focus >= range.StartDate && focus < range.EndExclusiveDate ? focus : due;
			var item = new PlanningWorkItem(source.TaskId, PlanningWorkKind.OrdinaryTask, date, null, null, PlanningWorkStatus.Completed, completion.CompletedAt);
			if (date >= range.StartDate && date < range.EndExclusiveDate) Add(1, item, source, date);
			if (completion.CompletedAt >= activityStartUtc && completion.CompletedAt < activityEndUtc)
				Add(2, item, source, LocalDate(completion.CompletedAt));
		}
		var allGroups = goals.Values.Concat(contexts.Values).Concat(days.Values).ToArray();
		var sampledSources = new[] { pending, scheduled, activity }.Concat(allGroups.SelectMany(g => g.Sections))
			.SelectMany(a => a.Samples).Select(s => s.Source).DistinctBy(s => s.TaskId).OrderBy(s => s.TaskId)
			.Select(s => new ReportWorkSource(s.TaskId, s.TaskListId, s.GoalId, s.ContextId, s.IsImportant, s.FocusAt, s.DueAt, s.Schedule is not null)).ToArray();
		return new(calendar, range, activityStartUtc, activityEndUtc, pending.Build(), scheduled.Build(), activity.Build(),
			goals.OrderBy(g => g.Key).Select(g => g.Value.Build(g.Key)).ToArray(),
			contexts.OrderBy(g => g.Key).Select(g => g.Value.Build(g.Key == Guid.Empty ? null : g.Key)).ToArray(),
			days.Select(g => new ReportWorkDay(g.Key, g.Value.Sections[0].Build(), g.Value.Sections[1].Build(), g.Value.Sections[2].Build())).ToArray(), sampledSources);

		DateOnly LocalDate(DateTime instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
			DateTime.SpecifyKind(instant, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZone)));
		void Add(int section, PlanningWorkItem item, PlanningWorkSource source, DateOnly? date)
		{
			new[] { pending, scheduled, activity }[section].Add(item, source, date);
			if (includeGroups)
			{
				if (source.GoalId is { } goal) Get(goals, goal).Sections[section].Add(item, source, date);
				Get(contexts, source.ContextId ?? Guid.Empty).Sections[section].Add(item, source, date);
			}
			// A decades-long retained prefix must not create an unbounded list of daily payloads.
			if (date >= range.StartDate && date < range.EndExclusiveDate)
			{
				if (!days.TryGetValue(date.Value, out var day)) days[date.Value] = day = new(sampleLimit, range.StartDate);
				day.Sections[section].Add(item, source, date);
			}
		}
		Group Get(Dictionary<Guid, Group> groups, Guid id)
		{
			if (!groups.TryGetValue(id, out var group)) groups[id] = group = new(sampleLimit, range.StartDate);
			return group;
		}
	}

	private static PlanningWorkItem FromFact(TaskOccurrenceFact fact, PlanningWorkSource source, PlanningPeriod calendar) =>
		new(source.TaskId, PlanningWorkKind.Occurrence, fact.Id.OccurrenceDate, fact.Id.OccurrenceDate,
			RecurrenceCalendar.PeriodContaining(source.Schedule!.Rule.Frequency, fact.Id.OccurrenceDate, calendar),
			fact.Status switch { TaskOccurrenceStatus.Completed => PlanningWorkStatus.Completed, TaskOccurrenceStatus.Skipped => PlanningWorkStatus.Skipped, _ => PlanningWorkStatus.Cancelled }, fact.CompletedAt);

	private sealed class Group(int limit, DateOnly start)
	{
		public Aggregate[] Sections { get; } = [new(limit, start), new(limit, start), new(limit, start)];
		public ReportWorkGroup Build(Guid? id) => new(id, Sections[0].Build(), Sections[1].Build(), Sections[2].Build());
	}
	private sealed record Sample(PlanningWorkItem Item, PlanningWorkSource Source, DateOnly? Date);
	private sealed class Aggregate(int limit, DateOnly start)
	{
		private int _ordinary, _occurrences, _pending, _pendingOccurrences, _completed, _skipped, _cancelled, _before;
		private readonly HashSet<Guid> _series = [];
		public List<Sample> Samples { get; } = [];
		public void Add(PlanningWorkItem item, PlanningWorkSource source, DateOnly? date)
		{
			checked
			{
				if (item.Kind == PlanningWorkKind.OrdinaryTask) _ordinary++; else { _occurrences++; _series.Add(item.TaskId); }
				switch (item.Status) { case PlanningWorkStatus.Pending: _pending++; if (item.Kind == PlanningWorkKind.Occurrence) _pendingOccurrences++; break; case PlanningWorkStatus.Completed: _completed++; break; case PlanningWorkStatus.Skipped: _skipped++; break; default: _cancelled++; break; }
				if (date < start) _before++;
			}
			if (limit == 0) return;
			Samples.Add(new(item, source, date));
			Samples.Sort((a, b) =>
			{
				var compare = Nullable.Compare(a.Date, b.Date);
				if (compare == 0) compare = a.Item.TaskId.CompareTo(b.Item.TaskId);
				return compare != 0 ? compare : Nullable.Compare(a.Item.OccurrenceDate, b.Item.OccurrenceDate);
			});
			if (Samples.Count > limit) Samples.RemoveAt(limit);
		}
		public ReportWorkSection Build() => new(new(_ordinary, _series.Count, _occurrences), _pending, _pendingOccurrences, _completed, _skipped, _cancelled, _before,
			limit, (long)_ordinary + _occurrences > Samples.Count, Samples.Select(s => new ReportWorkSample(s.Item.TaskId, s.Item.OccurrenceDate, s.Date,
				s.Item.Period, s.Item.Status, s.Item.CompletedAt, GeneralReportBuilder.Clip(s.Source.Title), s.Source.Title.Length > GeneralReportBuilder.TitleLimit)).ToArray());
	}
}

/// <summary>Shared interpretation guidance for report tools and mode preload context.</summary>
public static class ReportWorkGuidance
{
	public const string Instructions = "Use the additive Work block (General local questions: Calendar.Work) for mixed ordinary/recurring work. Legacy task fields count stored task records/series, not actionable occurrences. Work.Pending, Work.Scheduled and Work.CompletionActivity overlap: never sum them. Address an occurrence by original TaskId plus OccurrenceDate; do not deduplicate occurrences by TaskId. Scheduled selects original calendar dates; CompletionActivity selects actual CompletedAt instants, so a Friday occurrence completed Monday belongs to Friday outcomes and Monday activity. Source deadlines are separate and must not be multiplied by occurrences. Exact counts are independent of bounded samples; Truncated means omitted work. Recurrence dates and count concentration do not establish overdue deadlines, effort, overload or feasibility. Parent closure adds no completed work. Ending a series is different from completing an occurrence; no occurrence mutation is authorized by reading a report. Refresh reports after changes.";
}
