using System.Security.Cryptography;
using System.Text.Json;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tasks.Recurrence;

public sealed class RecurringTaskSuccessors(IRecurringTaskStore store) : IRecurringTaskSuccessors
{
	public Task<RecurringSuccessorPreview> PreviewAsync(Guid taskId, RecurringSuccessorRequest request, CancellationToken ct = default)
	{
		ValidateRequest(request);
		return store.ReadAsync(taskId, (snapshot, token) => Preview(snapshot, request, token), ct);
	}

	public Task<RecurringSuccessorResult> ApplyAsync(Guid taskId, Guid requestId, RecurringSuccessorRequest request,
		string previewToken, SuccessorConflictResolution? resolution = null, CancellationToken ct = default)
	{
		ValidateRequest(request);
		if (requestId == Guid.Empty) throw new ArgumentException("A request ID is required.", nameof(requestId));
		ArgumentException.ThrowIfNullOrWhiteSpace(previewToken);
		if (resolution.HasValue && !Enum.IsDefined(resolution.Value)) throw new ArgumentOutOfRangeException(nameof(resolution));
		var fingerprint = Hash(new { taskId, request, previewToken, resolution });
		return store.ExecuteAsync(taskId, async (tx, token) =>
		{
			RequireAccessible(tx);
			var prior = await tx.GetSuccessorOperationAsync(requestId, token);
			if (prior is not null)
			{
				if (prior.RequestFingerprint != fingerprint)
					throw new InvalidOperationException("Request ID was already used with different inputs.");
				return prior.Result;
			}
			var current = await Preview(tx, request, token);
			if (current.PreviewToken != previewToken)
				throw new InvalidOperationException("The successor preview changed; refresh it before applying a choice.");
			if (current.AffectedFacts.Count > 0 && resolution is null)
				throw new InvalidOperationException("Choose a later start or explicitly treat successor occurrences as separate work.");
			var schedule = resolution == SuccessorConflictResolution.MoveStartAfterFacts
				? current.LaterSchedule ?? throw new InvalidOperationException("No valid later start remains within the proposed bounds.")
				: current.ProposedSchedule;
			var successor = tx.Task.CreateRecurrenceSuccessor(schedule);
			var result = new RecurringSuccessorResult(taskId, successor.Id);
			tx.AddSuccessor(successor, new(requestId, fingerprint, result));
			return result;
		}, ct);
	}

	private static async Task<RecurringSuccessorPreview> Preview(IRecurringTaskSnapshot snapshot,
		RecurringSuccessorRequest request, CancellationToken ct)
	{
		RequireAccessible(snapshot);
		var task = snapshot.Task;
		if (task.RecurrenceSuccessorTaskId.HasValue)
			throw new InvalidOperationException("This series already has a successor; change the continuation instead.");
		if (task.IsCompleted != (request.Kind == RecurringSuccessorKind.Resume))
			throw new InvalidOperationException(task.IsCompleted ? "Use explicit resumption for a completed series." : "Use replacement for an open series.");
		var source = task.Recurrence!;
		var end = request.ClearEndDate ? null : request.EndDate ?? source.EndDate;
		if (request.Kind == RecurringSuccessorKind.Resume && request.EndDate is null && end < request.StartDate) end = null;
		var schedule = new RecurrenceSchedule(request.Rule ?? source.Rule, request.StartDate, end, request.MissedPolicy ?? source.MissedPolicy);
		var facts = await snapshot.GetFactsOnOrAfterAsync(request.StartDate, ct);
		RecurrenceSchedule? later = schedule;
		if (facts.Count > 0)
		{
			var last = facts.Max(f => f.Id.OccurrenceDate);
			later = last == DateOnly.MaxValue || end <= last ? null
				: new(schedule.Rule, last.AddDays(1), end, schedule.MissedPolicy);
		}
		// Include the parent revision and copied metadata as well as every affected fact. Facts
		// have independent writes, so a revision alone cannot validate an explicit conflict choice.
		var token = Hash(new { task.Id, task.Recurrence, task.RecurrenceRevision, task.IsCompleted,
			task.Title, task.Description, task.TaskListId, task.GoalId, task.IsImportant, task.DueAt, task.FocusAt,
			task.RecurrencePredecessorTaskId, task.RecurrenceSuccessorTaskId, request, schedule, facts });
		return new(new(task.Id, source, task.IsCompleted, task.RecurrencePredecessorTaskId, task.RecurrenceSuccessorTaskId),
			schedule, facts, later, token, source.ShortenBefore(schedule.StartDate),
			later is null ? null : source.ShortenBefore(later.StartDate));
	}

	private static void RequireAccessible(IRecurringTaskSnapshot snapshot)
	{
		if (snapshot.Task.TrashedAt.HasValue) throw new KeyNotFoundException("Recurring task not found.");
		if (snapshot.Task.Recurrence is null) throw new InvalidOperationException("Task is not recurring.");
		if (!snapshot.ListAvailable) throw new InvalidOperationException("The task list is unavailable.");
	}

	private static void ValidateRequest(RecurringSuccessorRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (!Enum.IsDefined(request.Kind)) throw new ArgumentOutOfRangeException(nameof(request));
		if (request.StartDate == DateOnly.MinValue) throw new ArgumentOutOfRangeException(nameof(request), "Successor start needs a representable preceding date.");
		if (request.ClearEndDate && request.EndDate.HasValue) throw new ArgumentException("Choose an end date or clear it, not both.", nameof(request));
	}

	private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
