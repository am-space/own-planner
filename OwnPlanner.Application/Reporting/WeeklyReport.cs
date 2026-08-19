namespace OwnPlanner.Application.Reporting;

public sealed record WeeklyReportOptions(
	DateOnly? StartDate = null,
	int TaskSampleLimit = 3,
	int OverloadedDayThreshold = 5)
{
	public const int MinSampleLimit = 0;
	public const int MaxSampleLimit = 5;
	public const int MinOverloadedDayThreshold = 1;
	public const int MaxOverloadedDayThreshold = 20;

	public void Validate()
	{
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
	WeeklyPlanningSignals Signals);

public sealed record WeeklyOverallTotals(
	int FocusedInsideWindowCount,
	int DueInsideWindowCount,
	int DistinctWindowTaskCount,
	int OverdueIncompleteTaskCount,
	int ImportantIncompleteTaskCount,
	int UnscheduledIncompleteTaskCount);

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

public sealed record WeeklyOverloadedDay(DateOnly Date, int DistinctTaskCount);

public sealed record WeeklyPlanningSignals(
	IReadOnlyList<WeeklyOverloadedDay> OverloadedDays,
	IReadOnlyList<StrategicEntityReference> ActiveGoalsWithoutFocusedWork,
	IReadOnlyList<StrategicTaskSample> OverdueTasksNotFocusedInsideWindow,
	int OverdueTasksNotFocusedInsideWindowCount,
	IReadOnlyList<StrategicTaskSample> ImportantTasksWithoutFocusDate,
	int ImportantTasksWithoutFocusDateCount);
