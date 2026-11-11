using OwnPlanner.Application.Calendar;

namespace OwnPlanner.Application.Reporting;

/// <summary>Bounds report samples and the task-count concentration heuristic; does not specify time capacity.</summary>
/// <param name="StartDate">Legacy UTC start date; mutually exclusive with CalendarPeriod.</param>
/// <param name="TaskSampleLimit">Maximum samples per view; exact counts do not depend on this limit.</param>
/// <param name="OverloadedDayThreshold">Inclusive distinct focus-or-due task count threshold (default 5), not an effort or feasibility limit.</param>
/// <param name="CalendarPeriod">Optional named period resolved through shared user-calendar rules.</param>
public sealed record WeeklyReportOptions(
	DateOnly? StartDate = null,
	int TaskSampleLimit = 3,
	int OverloadedDayThreshold = 5,
	string? CalendarPeriod = null)
{
	public const int MinSampleLimit = 0;
	public const int MaxSampleLimit = 5;
	public const int MinOverloadedDayThreshold = 1;
	public const int MaxOverloadedDayThreshold = 20;

	public void Validate()
	{
		if (CalendarPeriod is not null)
		{
			PlanningCalendarResolver.Validate(CalendarPeriod);
			if (StartDate.HasValue) throw new ArgumentException("Use calendarPeriod or startDate, not both.");
		}

		if (TaskSampleLimit is < MinSampleLimit or > MaxSampleLimit)
			throw new ArgumentOutOfRangeException(nameof(TaskSampleLimit), $"Task sample limit must be between {MinSampleLimit} and {MaxSampleLimit}.");
		if (OverloadedDayThreshold is < MinOverloadedDayThreshold or > MaxOverloadedDayThreshold)
			throw new ArgumentOutOfRangeException(nameof(OverloadedDayThreshold), $"Overloaded day threshold must be between {MinOverloadedDayThreshold} and {MaxOverloadedDayThreshold}.");
	}
}

public sealed record WeeklyReport(
	DateTime AsOfUtc,
	DateOnly WindowStartDate,
	DateOnly WindowEndExclusiveDate,
	string TimeZone,
	string WindowSemantics,
	int OverloadedDayThreshold,
	WeeklyOverallTotals Totals,
	IReadOnlyList<WeeklyDaySummary> Days,
	IReadOnlyList<WeeklyContextSummary> Contexts,
	IReadOnlyList<WeeklyGoalSummary> Goals,
	WeeklyPlanningSignals Signals)
{
	/// <summary>Present only for explicitly requested user-calendar periods; legacy UTC requests retain their meanings.</summary>
	public PlanningPeriod? Calendar { get; init; }
}

public sealed record WeeklyOverallTotals(
	int FocusedInsideWindowCount,
	int DueInsideWindowCount,
	int DistinctWindowTaskCount,
	int OverdueIncompleteTaskCount,
	int ImportantIncompleteTaskCount,
	int UnscheduledIncompleteTaskCount);

/// <summary>Separate focus/deadline evidence for a calendar day, without duration or availability estimates.</summary>
/// <param name="Date">The report calendar date.</param>
/// <param name="FocusedTaskCount">Number of tasks planned for this date.</param>
/// <param name="DueTaskCount">Number of deadline commitments on this date.</param>
/// <param name="DistinctTaskCount">Union of focused and due task identifiers; a task in both views counts once.</param>
/// <param name="IsOverloaded">Compatibility name for DistinctTaskCount &gt;= OverloadedDayThreshold; a concentration signal, not proof of overload or feasibility.</param>
/// <param name="FocusedTaskSamples">Bounded focus samples; not the complete task set.</param>
/// <param name="DueTaskSamples">Bounded deadline samples; not the complete task set.</param>
public sealed record WeeklyDaySummary(
	DateOnly Date,
	int FocusedTaskCount,
	int DueTaskCount,
	int DistinctTaskCount,
	bool IsOverloaded,
	IReadOnlyList<StrategicTaskSample> FocusedTaskSamples,
	IReadOnlyList<StrategicTaskSample> DueTaskSamples);

public sealed record WeeklyContextSummary(
	Guid? Id,
	string Name,
	bool IsMissingOrUnassigned,
	int WindowTaskCount,
	int FocusedInsideWindowCount,
	int DueInsideWindowCount,
	int ImportantIncompleteTaskCount,
	int OverdueIncompleteTaskCount,
	IReadOnlyList<StrategicTaskSample> TaskSamples);

public sealed record WeeklyGoalSummary(
	Guid Id,
	string Title,
	int WindowTaskCount,
	int FocusedInsideWindowCount,
	int DueInsideWindowCount,
	int ImportantIncompleteTaskCount,
	int OverdueIncompleteTaskCount,
	IReadOnlyList<StrategicTaskSample> TaskSamples);

/// <summary>A day meeting the inclusive task-count threshold; the legacy name makes no capacity diagnosis.</summary>
public sealed record WeeklyOverloadedDay(DateOnly Date, int DistinctTaskCount);

public sealed record WeeklyPlanningSignals(
	IReadOnlyList<WeeklyOverloadedDay> OverloadedDays,
	IReadOnlyList<StrategicEntityReference> ActiveGoalsWithoutFocusedWork,
	IReadOnlyList<StrategicTaskSample> OverdueTasksNotFocusedInsideWindow,
	int OverdueTasksNotFocusedInsideWindowCount,
	IReadOnlyList<StrategicTaskSample> ImportantTasksWithoutFocusDate,
	int ImportantTasksWithoutFocusDateCount);
