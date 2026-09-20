namespace OwnPlanner.Application.Reporting;

/// <summary>A bounded current-state snapshot. Task IDs reference the deduplicated Tasks table.</summary>
public sealed record GeneralReport(
	DateTime AsOfUtc, string TimeZone, DateOnly TodayDate, DateOnly UpcomingStartDate,
	DateOnly UpcomingEndExclusiveDate, GeneralToday Today, GeneralCommitments Commitments,
	GeneralInbox Inbox, GeneralDirection Direction, IReadOnlyList<GeneralTaskSample> Tasks,
	int TitleCharacterLimit);
public sealed record GeneralToday(int FocusTaskCount, int CompletedCount, GeneralSampleIds Remaining);
public sealed record GeneralSampleIds(int TotalCount, int SampleLimit, bool Truncated, IReadOnlyList<Guid> TaskIds);
public sealed record GeneralCommitments(int OverdueCount, int DueTodayCount, int UpcomingSevenDayCount, GeneralSampleIds Nearest);
public sealed record GeneralInbox(int UnreviewedCaptureCount, int UnscheduledTaskCount);
public sealed record GeneralDirection(int ActiveGoalCount, int LinkedGoalCount, int SampleLimit, bool Truncated, IReadOnlyList<GeneralGoalSample> Goals);
public sealed record GeneralGoalSample(Guid Id, string Title, bool TitleTruncated);
public sealed record GeneralTaskSample(Guid Id, string Title, bool TitleTruncated, DateTime? FocusAt, DateTime? DueAt);

/// <summary>Eligible task metadata supplied by the tenant-bound reader; excludes Trash and archived lists.</summary>
public sealed record GeneralTaskRow(Guid Id, string Title, bool IsCompleted, DateTime? FocusAt, DateTime? DueAt, Guid TaskListId, Guid? GoalId);
public sealed record GeneralGoalRow(Guid Id, string Title);

/// <summary>Deterministic UTC calendar rules and bounded samples, independent of persistence and transport.</summary>
public static class GeneralReportBuilder
{
	public const int TitleLimit = 80;
	public static GeneralReport Build(DateTime asOfUtc, IReadOnlyList<GeneralTaskRow> tasks,
		IReadOnlyList<GeneralGoalRow> activeGoals, int unreviewedCaptureCount)
	{
		var today = DateOnly.FromDateTime(asOfUtc);
		var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		var tomorrow = start.AddDays(1);
		var end = tomorrow.AddDays(7);
		var focus = tasks.Where(t => InWindow(t.FocusAt, start, tomorrow)).ToList();
		var incomplete = tasks.Where(t => !t.IsCompleted).ToList();
		var remaining = Order(focus.Where(t => !t.IsCompleted)).ToList();
		var commitments = Order(incomplete.Where(t => t.DueAt < end)).ToList();
		var focusIds = remaining.Take(5).Select(t => t.Id).ToArray();
		var deadlineIds = commitments.Take(3).Select(t => t.Id).ToArray();
		var sampleIds = focusIds.Concat(deadlineIds).ToHashSet();
		var linkedIds = focus.Concat(incomplete.Where(t => InWindow(t.DueAt, start, end)))
			.Where(t => t.GoalId.HasValue).Select(t => t.GoalId!.Value).ToHashSet();
		var linkedGoals = activeGoals.Where(g => linkedIds.Contains(g.Id))
			.OrderBy(g => g.Title, StringComparer.Ordinal).ThenBy(g => g.Id).ToList();
		return new GeneralReport(asOfUtc, "UTC", today, today.AddDays(1), today.AddDays(8),
			new(focus.Count, focus.Count(t => t.IsCompleted), new(remaining.Count, 5, remaining.Count > 5, focusIds)),
			new(incomplete.Count(t => t.DueAt < start), incomplete.Count(t => InWindow(t.DueAt, start, tomorrow)),
				incomplete.Count(t => InWindow(t.DueAt, tomorrow, end)), new(commitments.Count, 3, commitments.Count > 3, deadlineIds)),
			new(unreviewedCaptureCount, incomplete.Count(t => t.TaskListId == OwnPlanner.Domain.WellKnownIds.InboxTaskList && t.FocusAt is null && t.DueAt is null)),
			new(activeGoals.Count, linkedGoals.Count, 3, linkedGoals.Count > 3,
				linkedGoals.Take(3).Select(g => new GeneralGoalSample(g.Id, Clip(g.Title), g.Title.Length > TitleLimit)).ToArray()),
			Order(tasks.Where(t => sampleIds.Contains(t.Id))).Select(t => new GeneralTaskSample(t.Id, Clip(t.Title), t.Title.Length > TitleLimit, t.FocusAt, t.DueAt)).ToArray(), TitleLimit);
	}
	private static string Clip(string title) => title[..Math.Min(title.Length, TitleLimit)];
	private static bool InWindow(DateTime? value, DateTime start, DateTime end) => value >= start && value < end;
	private static IOrderedEnumerable<GeneralTaskRow> Order(IEnumerable<GeneralTaskRow> tasks) => tasks
		.OrderBy(t => t.DueAt ?? DateTime.MaxValue).ThenBy(t => t.FocusAt ?? DateTime.MaxValue).ThenBy(t => t.Id);
}
