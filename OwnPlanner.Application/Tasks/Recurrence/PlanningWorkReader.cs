using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public sealed class PlanningWorkReader(IPlanningWorkStore store, IPlanningCalendar calendar,
	IRecurrenceCalculator calculator) : IPlanningWorkReader
{
	public async Task<PlanningWorkPage> QueryAsync(PlanningWorkQuery query, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		RecurrenceReadLimits.ValidateRange(query.Range);
		RecurrenceReadLimits.ValidatePage(query.Offset, query.Limit);
		if (!Enum.IsDefined(query.OrdinaryTasks) || query.Status.HasValue && !Enum.IsDefined(query.Status.Value)
			|| query.Kind.HasValue && !Enum.IsDefined(query.Kind.Value)) throw new ArgumentException("Unsupported work selection.", nameof(query));
		var snapshot = await calendar.ResolveAsync("today", ct);
		return await store.ReadAsync(async (read, token) =>
		{
			var sources = await read.GetSourcesAsync(false, token);
			// Include the complete retained prefix, even when the requested window is far in the future.
			var first = query.IncludeRetainedBeforeStart
				? sources.Where(s => s.Schedule?.MissedPolicy == MissedOccurrencePolicy.RetainEachObligation)
					.Select(s => s.Schedule!.StartDate).Append(query.Range.StartDate).Min()
				: query.Range.StartDate;
			var facts = (await read.GetScheduledFactsAsync(new(first, query.Range.EndExclusiveDate), query.TaskId, token))
				.ToDictionary(f => f.Id);
			var byId = sources.ToDictionary(s => s.TaskId);
			var streams = sources.Select(s => Project(s, query, snapshot, facts, token));
			var page = new List<PlanningWorkItem>();
			var series = new HashSet<Guid>();
			var ordinaryCount = 0; var occurrenceCount = 0; var total = 0;
			foreach (var row in Merge(streams))
			{
				token.ThrowIfCancellationRequested();
				if (!Matches(row, byId[row.TaskId], query)) continue;
				if (total >= query.Offset && page.Count < query.Limit) page.Add(row);
				total = checked(total + 1);
				if (row.Kind == PlanningWorkKind.OrdinaryTask) ordinaryCount++;
				else { occurrenceCount = checked(occurrenceCount + 1); series.Add(row.TaskId); }
			}
			return new PlanningWorkPage(page, page.Select(r => byId[r.TaskId]).DistinctBy(s => s.TaskId).ToArray(),
				new(ordinaryCount, series.Count, occurrenceCount), query.Offset, query.Limit, snapshot, query.Range, query.IncludeRetainedBeforeStart);
		}, ct);
	}

	private IEnumerable<PlanningWorkItem> Project(PlanningWorkSource source, PlanningWorkQuery query,
		PlanningPeriod snapshot, IReadOnlyDictionary<TaskOccurrenceId, TaskOccurrenceFact> facts, CancellationToken ct)
	{
		if (source.Schedule is not { } schedule)
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
				yield return new(source.TaskId, PlanningWorkKind.OrdinaryTask, date, null, null, PlanningWorkStatus.Pending, null);
			}
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
