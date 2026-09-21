using OwnPlanner.Application.Tasks;

namespace OwnPlanner.Application.WeeklyReviews;

public sealed record WeeklyReviewActionResult(bool Applied, string Message, TaskItemDto? Task = null);

/// <summary>Rechecks a reviewed task before each explicitly authorized operation, reusing ordinary task services.</summary>
public sealed class WeeklyReviewActions(IWeeklyReviewStore store, ITaskItemService tasks, ITaskListService lists, TimeProvider clock)
{
	public async Task<WeeklyReviewActionResult> ApplyAsync(Guid reviewId, Guid taskId, DateTime revision, string action,
		DateOnly? focusDate = null, DateTime? dueAtUtc = null, CancellationToken ct = default)
	{
		var review = await store.UpdateAsync((_, reviews) => reviews.FirstOrDefault(r => r.Id == reviewId)
			?? throw new KeyNotFoundException("Weekly review not found."), ct);
		var now = clock.GetUtcNow().UtcDateTime;
		if (review.Status is "completed" or "skipped" || now >= review.EndsAtUtc)
			return new(false, "The review is finished or expired; open the current review.");
		var task = await tasks.GetAsync(taskId, ct);
		if (task is null || task.IsCompleted) return new(false, "Task is missing, completed or in Trash; no changes applied.");
		var list = await lists.GetAsync(task.TaskListId, ct);
		if (list is null || list.IsArchived) return new(false, "Task list is missing or archived; no changes applied.");
		if (task.UpdatedAt != revision) return new(false, "Task changed since review; refresh and resolve the changed target before retrying.");
		if (!new WeeklyReviewSelection(review, now).Contains(task.FocusAt, task.DueAt))
			return new(false, "Task no longer belongs to this review; no changes applied.");
		try
		{
			switch (action)
			{
				case "focus":
					if (focusDate is null || focusDate < review.TargetWeek || focusDate >= review.TargetWeek.AddDays(7))
						throw new ArgumentException("Choose a focus date within the target week.");
					await tasks.SetFocusDateAsync(taskId, focusDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ct); break;
				case "clearFocus": await tasks.ClearFocusDateAsync(taskId, ct); break;
				case "complete": await tasks.CompleteAsync(taskId, ct); break;
				case "trash": await tasks.DeleteAsync(taskId, ct); return new(true, "Moved to recoverable Trash.");
				case "clearDeadline": await tasks.UpdateAsync(taskId, ct: ct, clearDueAt: true); break;
				case "deadline":
					if (dueAtUtc is null) throw new ArgumentException("Specify the new deadline instant.");
					await tasks.UpdateAsync(taskId, dueAt: dueAtUtc, ct: ct); break;
				default: throw new ArgumentException("Action must be focus, clearFocus, complete, trash, deadline or clearDeadline.");
			}
			return new(true, "Change applied.", await tasks.GetAsync(taskId, ct));
		}
		catch (KeyNotFoundException) { return new(false, "Task disappeared before the change could be applied."); }
	}
}
