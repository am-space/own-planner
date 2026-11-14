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
		return await store.ExecuteAsync(id.TaskId, (tx, token) =>
			ApplyOccurrenceAsync(tx, id, snapshot, calculator, true, completedAtUtc, token), ct);
	}

	public async Task<TaskOccurrenceFact> CancelOccurrenceAsync(TaskOccurrenceId id, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(id);
		var snapshot = await calendar.ResolveAsync("today", ct);
		await ReconcileSeriesAsync(id.TaskId, snapshot, ct);
		return await store.ExecuteAsync(id.TaskId, (tx, token) =>
			ApplyOccurrenceAsync(tx, id, snapshot, calculator, false, null, token), ct);
	}

	/// <summary>Applies the shared terminal-fact policy inside a caller-owned tenant write transaction.
	/// Review callers perform their stale/review checks under the same lock before invoking this method.
	/// It never reconciles other dates, closes the parent, or commits the caller's transaction.</summary>
	public static async Task<TaskOccurrenceFact> ApplyOccurrenceAsync(IRecurringTaskTransaction tx, TaskOccurrenceId id,
		PlanningPeriod snapshot, IRecurrenceCalculator calculator, bool complete, DateTime? completedAtUtc = null, CancellationToken ct = default)
	{
		if (id.TaskId != tx.Task.Id) throw new ArgumentException("Occurrence does not belong to this transaction.", nameof(id));
		if (completedAtUtc.HasValue && (completedAtUtc.Value.Kind != DateTimeKind.Utc || completedAtUtc > snapshot.AsOfUtc))
			throw new ArgumentException("Completion time must be UTC and no later than the current clock.", nameof(completedAtUtc));
		RequireAccessibleSeries(tx);
		var fact = await tx.GetFactAsync(id.OccurrenceDate, ct);
		if (complete && fact?.Status == TaskOccurrenceStatus.Completed || !complete && fact?.Status == TaskOccurrenceStatus.Cancelled)
			return fact!;
		if (fact?.Status == TaskOccurrenceStatus.Cancelled || !complete && fact is not null)
			throw new InvalidOperationException("The recorded occurrence cannot make this transition.");
		// A recorded skip is a history correction, even on a closed predecessor.
		if (fact is null) RequireEligibleOccurrence(tx.Task, id.OccurrenceDate, calculator);
		if (!complete && tx.Task.Recurrence!.MissedPolicy == MissedOccurrencePolicy.SkipExpiredPeriods &&
			RecurrenceCalendar.PeriodContaining(tx.Task.Recurrence.Rule.Frequency, id.OccurrenceDate, snapshot).IsExpired(snapshot.AsOfUtc))
			throw new InvalidOperationException("The occurrence has expired; refresh before retrying.");
		var result = new TaskOccurrenceFact(id, complete ? TaskOccurrenceStatus.Completed : TaskOccurrenceStatus.Cancelled,
			complete ? completedAtUtc ?? snapshot.AsOfUtc : null);
		await tx.PutFactAsync(result, ct);
		return result;
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

	private static void RequireEligibleOccurrence(TaskItem task, DateOnly date, IRecurrenceCalculator calculator)
	{
		if (task.IsCompleted) throw new InvalidOperationException("The series has ended; only recorded skipped history can be corrected.");
		if (date == DateOnly.MaxValue || !calculator.Enumerate(task.Recurrence!, new(date, date.AddDays(1))).Any())
			throw new ArgumentException("Date is not an eligible occurrence.", nameof(date));
	}
}
