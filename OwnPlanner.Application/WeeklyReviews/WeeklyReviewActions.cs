using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Goals;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.WeeklyReviews;

public sealed record WeeklyReviewActionResult(bool Applied, string Message, TaskItemDto? Task = null)
{
	public TaskOccurrenceFact? Occurrence { get; init; }
}

/// <summary>Rechecks explicitly authorized ordinary actions and atomically guards shared occurrence transitions.</summary>
public sealed class WeeklyReviewActions(IWeeklyReviewStore store, ITaskItemService tasks, ITaskListService lists, TimeProvider clock, IGoalService goals)
{
	public async Task<WeeklyReviewActionResult> ApplyAsync(Guid reviewId, Guid taskId, DateTime revision, string action,
		DateOnly? focusDate = null, DateTime? dueAtUtc = null, CancellationToken ct = default, Guid? goalId = null, DateOnly? occurrenceDate = null, string? guardToken = null, DateTime? completedAtUtc = null)
	{
		if (completedAtUtc.HasValue && action != "completeOccurrence")
			throw new ArgumentException("completedAt is only valid for completeOccurrence.", nameof(completedAtUtc));
		if (occurrenceDate.HasValue || action is "completeOccurrence" or "cancelOccurrence")
			return await ApplyOccurrenceAsync(reviewId, taskId, revision, action, occurrenceDate, guardToken, completedAtUtc, ct);
		var review = await store.UpdateAsync((_, reviews) => reviews.FirstOrDefault(r => r.Id == reviewId)
			?? throw new KeyNotFoundException("Weekly review not found."), ct);
		var now = clock.GetUtcNow().UtcDateTime;
		if (review.Status is "completed" or "skipped" || now >= review.EndsAtUtc)
			return new(false, "The review is finished or expired; open the current review.");
		var task = await tasks.GetAsync(taskId, ct);
		if (task is null || task.IsCompleted) return new(false, "Task is missing, completed or in Trash; no changes applied.");
		if (task.IsRecurring)
			return new(false, "This is a recurring series. Use an explicit occurrence date and guard for occurrence actions; series changes require the separate series workflow.");
		var list = await lists.GetAsync(task.TaskListId, ct);
		if (list is null || list.IsArchived) return new(false, "Task list is missing or archived; no changes applied.");
		if (task.UpdatedAt != revision) return new(false, "Task changed since review; refresh and resolve the changed target before retrying.");
		if (action == "goalFocus")
		{
			if (goalId is null || task.GoalId != goalId) return new(false, "Task is not linked to the selected goal; no changes applied.");
			if ((await goals.GetAsync(goalId.Value, ct))?.Status != GoalStatus.Active)
				return new(false, "The goal is not active; no changes applied.");
		}
		else if (!new WeeklyReviewSelection(review, now).Contains(task.FocusAt, task.DueAt))
			return new(false, "Task no longer belongs to this review; no changes applied.");
		try
		{
			switch (action)
			{
				case "goalFocus":
				case "focus":
					if (focusDate is null || focusDate < review.TargetWeek || focusDate >= review.TargetWeek.AddDays(7))
						throw new ArgumentException("Choose a focus date within the target week.");
					await tasks.SetFocusDateAsync(taskId, focusDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct); break;
				case "clearFocus": await tasks.ClearFocusDateAsync(taskId, ct); break;
				case "complete": await tasks.CompleteAsync(taskId, ct); break;
				case "trash": await tasks.DeleteAsync(taskId, ct); return new(true, "Moved to recoverable Trash.");
				case "clearDeadline": await tasks.UpdateAsync(taskId, ct: ct, clearDueAt: true); break;
				case "linkGoal":
					if (goalId is null) throw new ArgumentException("Choose an active goal to link.");
					if (task.GoalId is not null) return new(false, "Task already has a goal; no changes applied.");
					if ((await goals.GetAsync(goalId.Value, ct))?.Status != GoalStatus.Active)
						return new(false, "The goal is not active; no changes applied.");
					await tasks.UpdateAsync(taskId, goalId: goalId, ct: ct); break;
				case "deadline":
					if (dueAtUtc is null) throw new ArgumentException("Specify the new deadline instant.");
					await tasks.UpdateAsync(taskId, dueAt: dueAtUtc, ct: ct); break;
				default: throw new ArgumentException("Action must be focus, goalFocus, linkGoal, clearFocus, complete, trash, deadline or clearDeadline.");
			}
			return new(true, "Change applied.", await tasks.GetAsync(taskId, ct));
		}
		catch (KeyNotFoundException) { return new(false, "Task disappeared before the change could be applied."); }
	}

	private async Task<WeeklyReviewActionResult> ApplyOccurrenceAsync(Guid reviewId, Guid taskId, DateTime revision,
		string action, DateOnly? occurrenceDate, string? guardToken, DateTime? completedAtUtc, CancellationToken ct)
	{
		if (action is not ("completeOccurrence" or "cancelOccurrence") || occurrenceDate is null || string.IsNullOrWhiteSpace(guardToken))
			throw new ArgumentException("Use completeOccurrence or cancelOccurrence with occurrenceDate and a fresh guardToken. Focus actions never reschedule occurrences.");
		try
		{
			return await store.ExecuteOccurrenceAsync(reviewId, taskId, async (review, tx, snapshot, token) =>
			{
				var now = clock.GetUtcNow().UtcDateTime;
				if (review.Status is "completed" or "skipped" || now >= review.EndsAtUtc)
					return new WeeklyReviewActionResult(false, "The review is finished or expired; open the current review.");
				if (tx.Task.TrashedAt.HasValue || tx.Task.Recurrence is null || !tx.ListAvailable || tx.Task.UpdatedAt.Ticks != revision.Ticks)
					return new WeeklyReviewActionResult(false, "The series or list changed; refresh before retrying.");
				var details = await WeeklyReviewOccurrences.ReadAsync(snapshot, review, now,
					new Dictionary<Guid, (DateTime, long)> { [taskId] = (tx.Task.UpdatedAt, tx.Task.RecurrenceRevision) },
					taskId, occurrenceDate: occurrenceDate, ct: token);
				var current = details.Items.SingleOrDefault();
				if (current is null || current.GuardToken != guardToken)
					return new WeeklyReviewActionResult(false, "Occurrence state or schedule changed; refresh before retrying.");
				var complete = action == "completeOccurrence";
				if (current.Work.Status != PlanningWorkStatus.Pending && !(complete && current.Work.Status == PlanningWorkStatus.Skipped))
					return new WeeklyReviewActionResult(false, "Occurrence is already resolved; refresh before retrying.");
				var fact = await RecurringTaskLifecycle.ApplyOccurrenceAsync(tx, new(taskId, occurrenceDate.Value),
					details.Calendar, new RecurrenceCalculator(), complete, completedAtUtc, token);
				return new WeeklyReviewActionResult(true, "Occurrence change applied; the series remains unchanged.") { Occurrence = fact };
			}, ct);
		}
		catch (KeyNotFoundException) { return new(false, "Review or occurrence is unavailable; refresh before retrying."); }
	}
}
