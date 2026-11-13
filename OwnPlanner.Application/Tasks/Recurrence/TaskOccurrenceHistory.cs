using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public sealed class TaskOccurrenceHistory(IPlanningWorkStore store, IPlanningCalendar calendar) : ITaskOccurrenceHistory
{
	public Task<TaskOccurrenceFact?> GetAsync(TaskOccurrenceId id, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		return store.ReadAsync((read, token) => read.GetFactAsync(id, token), ct);
	}

	public async Task<OccurrenceHistoryPage> QueryAsync(OccurrenceHistoryQuery query, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		RecurrenceReadLimits.ValidatePage(query.Offset, query.Limit);
		if (query.Basis == OccurrenceHistoryBasis.ScheduledDate)
		{
			if (query.StartUtc.HasValue || query.EndExclusiveUtc.HasValue)
				throw new ArgumentException("Scheduled history requires only calendar-date boundaries.", nameof(query));
			RecurrenceReadLimits.ValidateRange(query.ScheduledRange!);
		}
		else if (query.Basis == OccurrenceHistoryBasis.CompletionTime)
		{
			if (query.ScheduledRange is not null || query.StartUtc is not { Kind: DateTimeKind.Utc } start
				|| query.EndExclusiveUtc is not { Kind: DateTimeKind.Utc } end || end <= start
				|| end - start > TimeSpan.FromDays(RecurrenceReadLimits.MaximumRangeDays))
				throw new ArgumentException("Completion history requires a bounded UTC instant range only.", nameof(query));
		}
		else throw new ArgumentOutOfRangeException(nameof(query));
		var snapshot = await calendar.ResolveAsync("today", ct);
		return await store.ReadAsync(async (read, token) =>
		{
			var sources = (await read.GetSourcesAsync(true, token)).Where(s => s.Schedule is not null).ToDictionary(s => s.TaskId);
			var facts = query.Basis == OccurrenceHistoryBasis.ScheduledDate
				? await read.GetScheduledFactsAsync(query.ScheduledRange!, query.TaskId, token)
				: await read.GetCompletionFactsAsync(query.StartUtc!.Value, query.EndExclusiveUtc!.Value, query.TaskId, token);
			var available = facts.Where(f => sources.ContainsKey(f.Id.TaskId));
			var ordered = query.Basis == OccurrenceHistoryBasis.ScheduledDate
				? available.OrderBy(f => f.Id.OccurrenceDate).ThenBy(f => f.Id.TaskId)
				: available.OrderBy(f => f.CompletedAt).ThenBy(f => f.Id.TaskId).ThenBy(f => f.Id.OccurrenceDate);
			var items = new List<OccurrenceHistoryItem>(); var total = 0;
			foreach (var fact in ordered)
			{
				token.ThrowIfCancellationRequested();
				if (total >= query.Offset && items.Count < query.Limit)
					items.Add(new(fact, RecurrenceCalendar.PeriodContaining(sources[fact.Id.TaskId].Schedule!.Rule.Frequency,
						fact.Id.OccurrenceDate, snapshot)));
				total = checked(total + 1);
			}
			return new OccurrenceHistoryPage(items, items.Select(i => sources[i.Fact.Id.TaskId]).DistinctBy(s => s.TaskId).ToArray(),
				total, query.Offset, query.Limit, snapshot, query);
		}, ct);
	}

	public async Task<(IReadOnlyList<TaskOccurrenceFact> Items, int TotalCount)> ListByScheduledDateAsync(
		OccurrenceDateRange range, Guid? taskId, int offset, int limit, CancellationToken ct = default)
	{
		var page = await QueryAsync(new(OccurrenceHistoryBasis.ScheduledDate, range, TaskId: taskId, Offset: offset, Limit: limit), ct);
		return (page.Items.Select(i => i.Fact).ToArray(), page.TotalCount);
	}

	public async Task<(IReadOnlyList<TaskOccurrenceFact> Items, int TotalCount)> ListByCompletionAsync(
		DateTime startUtc, DateTime endExclusiveUtc, Guid? taskId, int offset, int limit, CancellationToken ct = default)
	{
		var page = await QueryAsync(new(OccurrenceHistoryBasis.CompletionTime, StartUtc: startUtc, EndExclusiveUtc: endExclusiveUtc,
			TaskId: taskId, Offset: offset, Limit: limit), ct);
		return (page.Items.Select(i => i.Fact).ToArray(), page.TotalCount);
	}
}
