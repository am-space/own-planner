using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Reporting;

public sealed record StrategicReportOptions(int TaskSampleLimit = 3, int NoteSampleLimit = 2)
{
	public const int MinSampleLimit = 0;
	public const int MaxSampleLimit = 5;

	public void Validate()
	{
		if (TaskSampleLimit is < MinSampleLimit or > MaxSampleLimit)
			throw new ArgumentOutOfRangeException(nameof(TaskSampleLimit), $"Task sample limit must be between {MinSampleLimit} and {MaxSampleLimit}.");
		if (NoteSampleLimit is < MinSampleLimit or > MaxSampleLimit)
			throw new ArgumentOutOfRangeException(nameof(NoteSampleLimit), $"Note sample limit must be between {MinSampleLimit} and {MaxSampleLimit}.");
	}
}

/// <summary>
/// A current-state snapshot at <c>AsOfUtc</c>, not historical activity evidence. Counts cover the
/// selected scope; task/note samples are bounded independently and text previews may be truncated.
/// </summary>
public sealed record StrategicReport(
	DateTime AsOfUtc,
	StrategicOverallTotals Totals,
	IReadOnlyList<StrategicContextSummary> Contexts,
	IReadOnlyList<StrategicGoalSummary> Goals,
	StrategicStructuralSignals Signals);

public sealed record StrategicOverallTotals(
	int ContextCount,
	int ActiveGoalCount,
	int TaskListCount,
	int NoteListCount,
	int IncompleteTaskCount,
	int ImportantIncompleteTaskCount,
	int OverdueIncompleteTaskCount,
	int NoteCount);

public sealed record StrategicContextSummary(
	Guid Id,
	string Name,
	ContextType Type,
	ContextStatus Status,
	int TaskListCount,
	int NoteListCount,
	int IncompleteTaskCount,
	int ImportantIncompleteTaskCount,
	int OverdueIncompleteTaskCount,
	int GoalLinkedTaskCount,
	int NoteCount,
	IReadOnlyList<StrategicTaskSample> TaskSamples,
	IReadOnlyList<StrategicNoteSample> NoteSamples);

public sealed record StrategicGoalSummary(
	Guid Id,
	string Title,
	GoalHorizon Horizon,
	string? TargetPeriod,
	DateTime? TargetDate,
	string? Metric,
	string? MetricCurrent,
	int IncompleteTaskCount,
	int ImportantIncompleteTaskCount,
	int OverdueIncompleteTaskCount,
	int DistinctContextCount,
	int DistinctTaskListCount,
	int LinkedNoteCount,
	IReadOnlyList<StrategicTaskSample> TaskSamples,
	IReadOnlyList<StrategicNoteSample> NoteSamples);

public sealed record StrategicTaskSample(
	Guid Id,
	string Title,
	string? DescriptionPreview,
	bool DescriptionTruncated,
	bool IsImportant,
	DateTime? DueAt,
	DateTime? FocusAt,
	Guid TaskListId,
	Guid? ContextId,
	Guid? GoalId);

public sealed record StrategicNoteSample(
	Guid Id,
	string Title,
	string? ContentPreview,
	bool ContentTruncated,
	bool IsPinned,
	DateTime LastUpdatedAt,
	Guid NoteListId,
	Guid? ContextId,
	Guid? GoalId);

public sealed record StrategicEntityReference(Guid Id, string Name);

/// <summary>
/// Structural observations, not established defects or severity ratings. "Active tasks" means
/// incomplete, non-trashed tasks in non-archived task lists. Goals are active; contexts non-archived.
/// </summary>
/// <param name="ActiveGoalsWithoutActiveTasks">Active goals with no eligible incomplete linked tasks;
/// completed-only and never-linked goals are indistinguishable here.</param>
/// <param name="ContextsWithoutActiveTasks">Non-archived contexts with no eligible incomplete tasks,
/// including contexts containing notes or completed work; does not establish inactivity duration.</param>
/// <param name="TasksWithoutGoal">Bounded sample of eligible incomplete tasks with a null or missing
/// goal reference. Existing inactive goal references are not missing. Operational tasks may be valid.</param>
/// <param name="TasksWithoutGoalCount">Full count in the selected task scope, independent of sample limit.</param>
/// <param name="ContextsWithoutTaskOrNoteLists">Non-archived contexts with neither non-archived task
/// nor note lists; may represent new setup or intentional emptiness.</param>
public sealed record StrategicStructuralSignals(
	IReadOnlyList<StrategicEntityReference> ActiveGoalsWithoutActiveTasks,
	IReadOnlyList<StrategicEntityReference> ContextsWithoutActiveTasks,
	IReadOnlyList<StrategicTaskSample> TasksWithoutGoal,
	int TasksWithoutGoalCount,
	IReadOnlyList<StrategicEntityReference> ContextsWithoutTaskOrNoteLists);
