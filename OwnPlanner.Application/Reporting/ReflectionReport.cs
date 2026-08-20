namespace OwnPlanner.Application.Reporting;

public sealed record ReflectionReportOptions(
	int PeriodDays = 7,
	DateTime? EndAtUtc = null,
	int TaskSampleLimit = 3,
	int NoteSampleLimit = 3)
{
	public const int MinPeriodDays = 1;
	public const int MaxPeriodDays = 31;
	public const int MinSampleLimit = 0;
	public const int MaxSampleLimit = 5;

	public void Validate()
	{
		if (PeriodDays is < MinPeriodDays or > MaxPeriodDays)
			throw new ArgumentOutOfRangeException(nameof(PeriodDays), $"Period days must be between {MinPeriodDays} and {MaxPeriodDays}.");
		if (EndAtUtc.HasValue && EndAtUtc.Value.Kind != DateTimeKind.Utc)
			throw new ArgumentException("End instant must be UTC.", nameof(EndAtUtc));
		if (TaskSampleLimit is < MinSampleLimit or > MaxSampleLimit)
			throw new ArgumentOutOfRangeException(nameof(TaskSampleLimit), $"Task sample limit must be between {MinSampleLimit} and {MaxSampleLimit}.");
		if (NoteSampleLimit is < MinSampleLimit or > MaxSampleLimit)
			throw new ArgumentOutOfRangeException(nameof(NoteSampleLimit), $"Note sample limit must be between {MinSampleLimit} and {MaxSampleLimit}.");
	}
}

public sealed record ReflectionReport(
	DateTime AsOfUtc,
	DateTime PeriodStartUtc,
	DateTime PeriodEndExclusiveUtc,
	string TimeZone,
	string PeriodSemantics,
	IReadOnlyList<string> HistoricalLimitations,
	ReflectionOverallTotals Totals,
	IReadOnlyList<ReflectionContextSummary> Contexts,
	IReadOnlyList<ReflectionGoalSummary> Goals,
	ReflectionInboxSummary Inbox,
	ReflectionSignals Signals);

public sealed record ReflectionOverallTotals(
	int CompletedTaskCount,
	int CreatedTaskCount,
	int MissedFocusTaskCount,
	int OverdueIncompleteTaskCount,
	int CreatedOrUpdatedNoteCount,
	int CurrentInboxNoteCount);

public sealed record ReflectionTaskSample(
	Guid Id,
	string Title,
	string? DescriptionPreview,
	bool DescriptionTruncated,
	bool IsImportant,
	DateTime CreatedAt,
	DateTime? CompletedAt,
	DateTime? DueAt,
	DateTime? FocusAt,
	Guid TaskListId,
	Guid? ContextId,
	Guid? GoalId);

public sealed record ReflectionContextSummary(
	Guid? Id,
	string Name,
	bool IsMissingOrUnassigned,
	int CompletedTaskCount,
	int MissedFocusTaskCount,
	IReadOnlyList<ReflectionTaskSample> CompletedTaskSamples,
	IReadOnlyList<ReflectionTaskSample> MissedFocusTaskSamples);

public sealed record ReflectionGoalSummary(
	Guid Id,
	string Title,
	int CompletedTaskCount,
	int RemainingIncompleteTaskCount,
	int RemainingOverdueTaskCount,
	int MissedFocusTaskCount,
	IReadOnlyList<ReflectionTaskSample> CompletedTaskSamples,
	IReadOnlyList<ReflectionTaskSample> MissedFocusTaskSamples);

public sealed record ReflectionInboxSummary(
	Guid NoteListId,
	int CurrentNoteCount,
	IReadOnlyList<StrategicNoteSample> NoteSamples);

public sealed record ReflectionSignals(
	IReadOnlyList<ReflectionTaskSample> FocusedButIncompleteTasks,
	int FocusedButIncompleteTaskCount,
	IReadOnlyList<StrategicEntityReference> ActiveGoalsWithoutCompletedWork,
	IReadOnlyList<ReflectionTaskSample> OverdueCarryoverTasks,
	int OverdueCarryoverTaskCount,
	int CurrentInboxNoteCount);
