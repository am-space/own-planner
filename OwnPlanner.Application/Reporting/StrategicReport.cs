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

public sealed record StrategicStructuralSignals(
	IReadOnlyList<StrategicEntityReference> ActiveGoalsWithoutActiveTasks,
	IReadOnlyList<StrategicEntityReference> ContextsWithoutActiveTasks,
	IReadOnlyList<StrategicTaskSample> TasksWithoutGoal,
	int TasksWithoutGoalCount,
	IReadOnlyList<StrategicEntityReference> ContextsWithoutTaskOrNoteLists);
