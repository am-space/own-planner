using System.Linq.Expressions;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.WeeklyReviews;

namespace OwnPlanner.Application.WeeklyReviews;

public sealed record WeeklyReviewTaskRow(Guid Id, string Title, Guid TaskListId, DateTime? FocusAt, DateTime? DueAt, DateTime Revision);

/// <summary>Application-owned selection rules, exposed as expressions so persistence can count and page in SQL.</summary>
public sealed class WeeklyReviewSelection(WeeklyReviewState review, DateTime nowUtc)
{
	private readonly DateTime _focusBefore = review.TargetWeek.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
	private readonly DateTime _start = review.StartsAtUtc;
	private readonly DateTime _end = review.EndsAtUtc;

	public Expression<Func<TaskItem, bool>> Carryover => t => t.FocusAt != null && t.FocusAt < _focusBefore;
	public Expression<Func<TaskItem, bool>> Overdue => t => t.DueAt != null && t.DueAt < nowUtc;
	public Expression<Func<TaskItem, bool>> DueInTargetWeek => t => t.DueAt != null && t.DueAt >= _start && t.DueAt < _end;
	public Expression<Func<TaskItem, bool>> Included => t =>
		t.FocusAt != null && t.FocusAt < _focusBefore || t.DueAt != null &&
		(t.DueAt < nowUtc || t.DueAt >= _start && t.DueAt < _end);

	public bool Contains(DateTime? focusAt, DateTime? dueAt) =>
		focusAt < _focusBefore || dueAt < nowUtc || dueAt >= _start && dueAt < _end;

	public WeeklyReviewReport BuildReport(int carryoverCount, int overdueCount, int targetWeekCount, int totalCount,
		int offset, int limit, IReadOnlyList<WeeklyReviewTaskRow> rows) =>
		new(nowUtc, carryoverCount, overdueCount, targetWeekCount, totalCount, offset, limit,
			rows.Select(t => new WeeklyReviewTask(t.Id, t.Title, t.TaskListId, t.FocusAt, t.DueAt, t.Revision,
				t.FocusAt < _focusBefore, t.DueAt < nowUtc, t.DueAt >= _start && t.DueAt < _end)).ToArray());
}
