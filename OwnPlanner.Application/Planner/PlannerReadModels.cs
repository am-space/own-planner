using OwnPlanner.Application.Common;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using System.Text.Json.Serialization;

namespace OwnPlanner.Application.Planner;

public static class PlannerReadDefaults
{
	public const int DefaultPageSize = 25;
	public const int MaximumPageSize = 100;
	public const int PreviewLength = 240;
}

public enum PlannerTaskStatus
{
	Open,
	Completed,
	All
}

public enum PlannerGoalStatus
{
	Active,
	Achieved,
	Dropped,
	All
}

public sealed record PlannerTaskQuery(
	string? Search = null,
	PlannerTaskStatus Status = PlannerTaskStatus.Open,
	bool ImportantOnly = false,
	Guid? TaskListId = null,
	Guid? ContextId = null,
	Guid? GoalId = null,
	int Offset = 0,
	int Limit = PlannerReadDefaults.DefaultPageSize);

public sealed record PlannerGoalQuery(
	string? Search = null,
	PlannerGoalStatus Status = PlannerGoalStatus.Active,
	GoalHorizon? Horizon = null,
	int Offset = 0,
	int Limit = PlannerReadDefaults.DefaultPageSize);

public sealed record PlannerNoteQuery(
	string? Search = null,
	bool PinnedOnly = false,
	Guid? NoteListId = null,
	Guid? ContextId = null,
	Guid? GoalId = null,
	int Offset = 0,
	int Limit = PlannerReadDefaults.DefaultPageSize);

public sealed record PlannerTaskSummaryDto(
	Guid Id,
	string Title,
	string? DescriptionPreview,
	bool IsCompleted,
	bool IsImportant,
	DateTime? DueAt,
	DateTime? FocusAt,
	DateTime UpdatedAt,
	Guid TaskListId,
	string TaskListName,
	Guid? ContextId,
	string? ContextName,
	Guid? GoalId,
	string? GoalName);

public sealed record PlannerTaskDetailDto(
	Guid Id,
	string Title,
	string? Description,
	bool IsCompleted,
	bool IsImportant,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	DateTime? DueAt,
	DateTime? CompletedAt,
	DateTime? FocusAt,
	Guid TaskListId,
	string TaskListName,
	Guid? ContextId,
	string? ContextName,
	Guid? GoalId,
	string? GoalName);

public sealed record PlannerGoalSummaryDto(
	Guid Id,
	string Title,
	string? DescriptionPreview,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] GoalHorizon Horizon,
	string? TargetPeriod,
	DateTime? TargetDate,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] GoalStatus Status,
	string? Metric,
	string? MetricCurrent,
	DateTime UpdatedAt);

public sealed record PlannerGoalDetailDto(
	Guid Id,
	string Title,
	string? Description,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] GoalHorizon Horizon,
	string? TargetPeriod,
	DateTime? TargetDate,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] GoalStatus Status,
	string? Metric,
	string? MetricCurrent,
	DateTime CreatedAt,
	DateTime UpdatedAt);

public sealed record PlannerNoteSummaryDto(
	Guid Id,
	string Title,
	string? ContentPreview,
	bool IsPinned,
	DateTime UpdatedAt,
	Guid NoteListId,
	string NoteListName,
	Guid? ContextId,
	string? ContextName,
	Guid? GoalId,
	string? GoalName);

public sealed record PlannerNoteDetailDto(
	Guid Id,
	string Title,
	string? Content,
	bool IsPinned,
	DateTime CreatedAt,
	DateTime UpdatedAt,
	Guid NoteListId,
	string NoteListName,
	Guid? ContextId,
	string? ContextName,
	Guid? GoalId,
	string? GoalName);

public sealed record PlannerListOptionDto(
	Guid Id,
	string Name,
	string? Color,
	bool IsArchived);

public sealed record PlannerContextOptionDto(
	Guid Id,
	string Name,
	string? Color,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] ContextStatus Status);

public sealed record PlannerGoalOptionDto(
	Guid Id,
	string Name,
	[property: JsonConverter(typeof(JsonStringEnumConverter))] GoalStatus Status);

public sealed record PlannerFilterOptionsDto(
	IReadOnlyList<PlannerListOptionDto> TaskLists,
	IReadOnlyList<PlannerListOptionDto> NoteLists,
	IReadOnlyList<PlannerContextOptionDto> Contexts,
	IReadOnlyList<PlannerGoalOptionDto> Goals);
