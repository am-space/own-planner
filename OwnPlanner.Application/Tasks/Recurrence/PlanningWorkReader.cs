using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public sealed class PlanningWorkReader(IPlanningWorkStore store, IPlanningCalendar calendar,
	IRecurrenceCalculator calculator) : IPlanningWorkReader
{
	public async Task<PlanningWorkPage> QueryAsync(PlanningWorkQuery query, CancellationToken ct = default)
	{
		Validate(query);
		var snapshot = await calendar.ResolveAsync("today", ct);
		return await store.ReadAsync(async (read, token) =>
		{
			var page = new List<PlanningWorkItem>();
			var total = 0;
			var sources = new Dictionary<Guid, PlanningWorkSource>();
			var counts = await VisitAsync(query, snapshot, read, calculator, (row, source) =>
			{
				if (total++ >= query.Offset && page.Count < query.Limit) { page.Add(row); sources[source.TaskId] = source; }
			}, token);
			return new PlanningWorkPage(page, page.Select(r => sources[r.TaskId]).DistinctBy(s => s.TaskId).ToArray(),
				counts, query.Offset, query.Limit, snapshot, query.Range, query.IncludeRetainedBeforeStart);
		}, ct);
	}

	/// <summary>Visits every selected row in shared projection order within a caller-owned tenant
	/// snapshot and resolved calendar. Consumers can aggregate exact counts with bounded samples
	/// without materializing or repeatedly paging the complete retained prefix. Visits the full filtered
	/// selection before Offset/Limit; validates their bounds but does not page. Never writes.</summary>
	public static async Task<PlanningWorkCounts> VisitAsync(PlanningWorkQuery query, PlanningPeriod snapshot,
		IPlanningWorkSnapshot read, IRecurrenceCalculator calculator,
		Action<PlanningWorkItem, PlanningWorkSource> visit, CancellationToken token = default)
	{
		Validate(query);
		var sources = (await read.GetSourcesAsync(false, token))
			.Where(s => !query.TaskId.HasValue || s.TaskId == query.TaskId.Value).ToArray();
		// Include the complete retained prefix, even when the requested window is far in the future.
		var first = query.IncludeRetainedBeforeStart
			? sources.Where(s => s.Schedule?.MissedPolicy == MissedOccurrencePolicy.RetainEachObligation)
				.Select(s => s.Schedule!.StartDate).Append(query.Range.StartDate).Min()
			: query.Range.StartDate;
		var facts = (await read.GetScheduledFactsAsync(new(first, query.Range.EndExclusiveDate), query.TaskId, token))
			.ToDictionary(f => f.Id);
		var byId = sources.ToDictionary(s => s.TaskId);
		var streams = sources.Select(s => Project(s, query, snapshot, facts, calculator, token));
		var series = new HashSet<Guid>();
		var ordinaryCount = 0; var occurrenceCount = 0;
		foreach (var row in Merge(streams))
		{
			token.ThrowIfCancellationRequested();
			var source = byId[row.TaskId];
			if (!Matches(row, source, query)) continue;
			visit(row, source);
			if (row.Kind == PlanningWorkKind.OrdinaryTask) ordinaryCount = checked(ordinaryCount + 1);
			else { occurrenceCount = checked(occurrenceCount + 1); series.Add(row.TaskId); }
		}
		return new(ordinaryCount, series.Count, occurrenceCount);
	}

	private static void Validate(PlanningWorkQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);
		RecurrenceReadLimits.ValidateRange(query.Range);
		RecurrenceReadLimits.ValidatePage(query.Offset, query.Limit);
		if (!Enum.IsDefined(query.OrdinaryTasks) || query.Status.HasValue && !Enum.IsDefined(query.Status.Value)
			|| query.Kind.HasValue && !Enum.IsDefined(query.Kind.Value)) throw new ArgumentException("Unsupported work selection.", nameof(query));
	}

	private static IEnumerable<PlanningWorkItem> Project(PlanningWorkSource source, PlanningWorkQuery query,
		PlanningPeriod snapshot, IReadOnlyDictionary<TaskOccurrenceId, TaskOccurrenceFact> facts, IRecurrenceCalculator calculator, CancellationToken ct)
	{
		if (source.Schedule is not { } schedule)
		{
			var ordinary = ProjectOrdinary(source, query, snapshot);
			if (ordinary is not null) yield return ordinary;
			yield break;
		}
		var start = query.IncludeRetainedBeforeStart && schedule.MissedPolicy == MissedOccurrencePolicy.RetainEachObligation
			&& schedule.StartDate < query.Range.StartDate ? schedule.StartDate : query.Range.StartDate;
		foreach (var date in calculator.Enumerate(schedule, new(start, query.Range.EndExclusiveDate)))
		{
			ct.ThrowIfCancellationRequested();
			facts.TryGetValue(new(source.TaskId, date), out var fact);
			if (date < query.Range.StartDate && fact is not null) continue;
			var period = RecurrenceCalendar.PeriodContaining(schedule.Rule.Frequency, date, snapshot);
			var status = fact is not null ? fact.Status switch
			{
				TaskOccurrenceStatus.Completed => PlanningWorkStatus.Completed,
				TaskOccurrenceStatus.Skipped => PlanningWorkStatus.Skipped,
				_ => PlanningWorkStatus.Cancelled
			} : schedule.MissedPolicy == MissedOccurrencePolicy.SkipExpiredPeriods && period.IsExpired(snapshot.AsOfUtc)
				? PlanningWorkStatus.Skipped : PlanningWorkStatus.Pending;
			yield return new(source.TaskId, PlanningWorkKind.Occurrence, date, date, period, status, fact?.CompletedAt);
		}
	}

	/// <summary>Selects an ordinary row and its representative date against the supplied range.
	/// Reports reuse this with their original range when occurrence generation expands earlier.</summary>
	internal static PlanningWorkItem? ProjectOrdinary(PlanningWorkSource source, PlanningWorkQuery query, PlanningPeriod snapshot)
	{
		var focus = source.FocusAt is { } f ? DateOnly.FromDateTime(f) : (DateOnly?)null;
		var due = source.DueAt is { } d ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(d,
			TimeZoneInfo.FindSystemTimeZoneById(snapshot.TimeZone))) : (DateOnly?)null;
		var focusMatches = InRange(focus, query.Range); var dueMatches = InRange(due, query.Range);
		var selected = query.OrdinaryTasks switch
		{
			OrdinaryTaskSelection.AllOpen => true,
			OrdinaryTaskSelection.FocusInRange => focusMatches,
			OrdinaryTaskSelection.DeadlineInRange => dueMatches,
			OrdinaryTaskSelection.FocusOrDeadlineInRange => focusMatches || dueMatches,
			_ => false
		};
		if (selected)
		{
			var date = query.OrdinaryTasks == OrdinaryTaskSelection.DeadlineInRange ? due
				: focusMatches ? focus : dueMatches ? due : focus ?? due;
			return new(source.TaskId, PlanningWorkKind.OrdinaryTask, date, null, null, PlanningWorkStatus.Pending, null);
		}
		return null;
	}

	private static bool InRange(DateOnly? date, OccurrenceDateRange range) => date >= range.StartDate && date < range.EndExclusiveDate;
	private static bool Matches(PlanningWorkItem row, PlanningWorkSource source, PlanningWorkQuery query) =>
		(!query.Status.HasValue || row.Status == query.Status) && (!query.Kind.HasValue || row.Kind == query.Kind)
		&& (!query.TaskId.HasValue || source.TaskId == query.TaskId)
		&& (!query.TaskListId.HasValue || source.TaskListId == query.TaskListId)
		&& (!query.GoalId.HasValue || source.GoalId == query.GoalId)
		&& (!query.ContextId.HasValue || source.ContextId == query.ContextId)
		&& (!query.ImportantOnly || source.IsImportant)
		&& (string.IsNullOrWhiteSpace(query.Search) || source.Title.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
			|| source.Description?.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase) == true);

	// Every source produces ordered dates. Keep only its next row, rather than materializing a
	// potentially decades-long retained prefix merely to sort it before counting and paging.
	private static IEnumerable<PlanningWorkItem> Merge(IEnumerable<IEnumerable<PlanningWorkItem>> streams)
	{
		var queue = new PriorityQueue<IEnumerator<PlanningWorkItem>, (int Date, Guid Task, PlanningWorkKind Kind)>();
		var iterators = new List<IEnumerator<PlanningWorkItem>>();
		try
		{
			foreach (var stream in streams)
			{
				var iterator = stream.GetEnumerator(); iterators.Add(iterator);
				if (iterator.MoveNext()) Enqueue(iterator);
			}
			while (queue.TryDequeue(out var iterator, out _))
			{
				yield return iterator.Current;
				if (iterator.MoveNext()) Enqueue(iterator);
			}
		}
		finally { foreach (var iterator in iterators) iterator.Dispose(); }

		void Enqueue(IEnumerator<PlanningWorkItem> iterator)
		{
			var row = iterator.Current;
			queue.Enqueue(iterator, (row.WorkDate?.DayNumber ?? int.MaxValue, row.TaskId, row.Kind));
		}
	}
}
