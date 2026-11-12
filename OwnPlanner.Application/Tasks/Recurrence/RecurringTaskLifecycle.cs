using OwnPlanner.Application.Calendar;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

/// <summary>Shared recurrence write policy. Reads never call this service implicitly.</summary>
public sealed class RecurringTaskLifecycle(IRecurringTaskStore store, IPlanningCalendar calendar,
	IRecurrenceCalculator calculator) : IRecurringTaskLifecycle
{
	public const int BatchSize = 256;

	public async Task<TaskOccurrenceFact> CompleteOccurrenceAsync(TaskOccurrenceId id, DateTime? completedAtUtc = null, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		var snapshot = await calendar.ResolveAsync("today", ct);
		if (completedAtUtc.HasValue && (completedAtUtc.Value.Kind != DateTimeKind.Utc || completedAtUtc > snapshot.AsOfUtc))
			throw new ArgumentException("Completion time must be UTC and no later than the current clock.", nameof(completedAtUtc));
		await ReconcileSeriesAsync(id.TaskId, snapshot, ct);
		return await store.ExecuteAsync(id.TaskId, async (tx, token) =>
		{
			RequireAccessibleSeries(tx);
			var fact = await tx.GetFactAsync(id.OccurrenceDate, token);
			if (fact?.Status == TaskOccurrenceStatus.Completed) return fact;
			if (fact?.Status == TaskOccurrenceStatus.Cancelled)
				throw new InvalidOperationException("A cancelled occurrence cannot be completed.");
			// A recorded skip is an explicit history correction, even on a closed predecessor.
			if (fact is null) RequireEligibleOccurrence(tx.Task, id.OccurrenceDate);
			var completed = new TaskOccurrenceFact(id, TaskOccurrenceStatus.Completed, completedAtUtc ?? snapshot.AsOfUtc);
			await tx.PutFactAsync(completed, token);
			return completed;
		}, ct);
	}

	public async Task<TaskOccurrenceFact> CancelOccurrenceAsync(TaskOccurrenceId id, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		var snapshot = await calendar.ResolveAsync("today", ct);
		await ReconcileSeriesAsync(id.TaskId, snapshot, ct);
		return await store.ExecuteAsync(id.TaskId, async (tx, token) =>
		{
			RequireAccessibleSeries(tx);
			var fact = await tx.GetFactAsync(id.OccurrenceDate, token);
			if (fact?.Status == TaskOccurrenceStatus.Cancelled) return fact;
			if (fact is not null) throw new InvalidOperationException("Only an unresolved occurrence can be cancelled.");
			RequireEligibleOccurrence(tx.Task, id.OccurrenceDate);
			var cancelled = new TaskOccurrenceFact(id, TaskOccurrenceStatus.Cancelled);
			await tx.PutFactAsync(cancelled, token);
			return cancelled;
		}, ct);
	}

	public async Task EndSeriesAsync(Guid taskId, CancellationToken ct = default)
	{
		var snapshot = await calendar.ResolveAsync("today", ct);
		await ReconcileSeriesAsync(taskId, snapshot, ct);
		await store.ExecuteAsync(taskId, (tx, _) =>
		{
			RequireAccessibleSeries(tx);
			tx.Task.Complete();
			return Task.FromResult(true);
		}, ct);
	}

	public async Task ReconcileAsync(CancellationToken ct = default)
	{
		var snapshot = await calendar.ResolveAsync("today", ct);
		foreach (var id in await store.ListOpenSeriesIdsAsync(ct))
		{
			ct.ThrowIfCancellationRequested();
			// Deletion between the ID snapshot and the transaction is harmless.
			try { await ReconcileSeriesAsync(id, snapshot, ct); }
			catch (KeyNotFoundException) { }
		}
	}

	public async Task TrashSeriesAsync(Guid taskId, CancellationToken ct = default)
	{
		var snapshot = await calendar.ResolveAsync("today", ct);
		await ReconcileSeriesAsync(taskId, snapshot, ct);
		await store.ExecuteAsync(taskId, (tx, _) =>
		{
			if (tx.Task.Recurrence is null) throw new InvalidOperationException("Task is not recurring.");
			tx.Task.Trash();
			return Task.FromResult(true);
		}, ct);
	}

	private async Task ReconcileSeriesAsync(Guid id, PlanningPeriod snapshot, CancellationToken ct)
	{
		while (await store.ExecuteAsync(id, (tx, token) => ReconcileBatchAsync(tx, snapshot, token), ct))
			ct.ThrowIfCancellationRequested();
	}

	private async Task<bool> ReconcileBatchAsync(IRecurringTaskTransaction tx, PlanningPeriod snapshot, CancellationToken ct)
	{
		var task = tx.Task;
		var schedule = task.Recurrence;
		if (schedule is null || task.IsCompleted || task.TrashedAt.HasValue) return false;
		var key = $"{snapshot.TimeZone}|{snapshot.WeekStart}";
		var cursor = task.RecurrenceCalendarKey == key ? task.RecurrenceCheckedBefore ?? schedule.StartDate : schedule.StartDate;
		DateOnly end;
		if (schedule.MissedPolicy == MissedOccurrencePolicy.RetainEachObligation)
		{
			if (schedule.EndDate is not { } bound || bound >= snapshot.Today) return false;
			end = bound.AddDays(1);
		}
		else
		{
			// Only dates before the current *full* period can have expired. A series cutoff does
			// not shorten the original period, including across timezone/DST preference changes.
			end = RecurrenceCalendar.PeriodContaining(schedule.Rule.Frequency, snapshot.Today, snapshot).Dates.StartDate;
			if (schedule.EndDate is { } bound && bound < end) end = bound.AddDays(1);
		}
		var more = false;
		if (cursor < end)
		{
			using var dates = calculator.Enumerate(schedule, new(cursor, end)).GetEnumerator();
			var count = 0;
			while (count < BatchSize && dates.MoveNext())
			{
				ct.ThrowIfCancellationRequested();
				var date = dates.Current;
				var fact = await tx.GetFactAsync(date, ct);
				if (fact is null)
				{
					if (schedule.MissedPolicy == MissedOccurrencePolicy.RetainEachObligation)
					{
						task.CheckRecurrenceBefore(date, key);
						return false;
					}
					await tx.PutFactAsync(new(new(task.Id, date), TaskOccurrenceStatus.Skipped), ct);
				}
				cursor = date.AddDays(1);
				count++;
			}
			more = count == BatchSize && dates.MoveNext();
			if (!more) cursor = end;
			task.CheckRecurrenceBefore(cursor, key);
		}
		// Empty bounded schedules may close as well. Retained work never closes on a mere end date.
		if (!more && schedule.EndDate is { } last && last < snapshot.Today)
		{
			var start = cursor > schedule.StartDate ? cursor : schedule.StartDate;
			// At most the current full period remains after the processed prefix. Explicit early
			// completion/cancellation can resolve it before period expiry, permitting timely closure.
			if (start <= last)
				foreach (var date in calculator.Enumerate(schedule, new(start, last.AddDays(1))))
					if (await tx.GetFactAsync(date, ct) is null) return false;
			task.Complete();
		}
		return more;
	}

	private static void RequireAccessibleSeries(IRecurringTaskTransaction tx)
	{
		if (tx.Task.TrashedAt.HasValue) throw new KeyNotFoundException("Recurring task not found.");
		if (tx.Task.Recurrence is null) throw new InvalidOperationException("Task is not recurring.");
		if (!tx.ListAvailable) throw new InvalidOperationException("The task list is unavailable.");
	}

	private void RequireEligibleOccurrence(TaskItem task, DateOnly date)
	{
		if (task.IsCompleted) throw new InvalidOperationException("The series has ended; only recorded skipped history can be corrected.");
		if (date == DateOnly.MaxValue || !calculator.Enumerate(task.Recurrence!, new(date, date.AddDays(1))).Any())
			throw new ArgumentException("Date is not an eligible occurrence.", nameof(date));
	}
}
