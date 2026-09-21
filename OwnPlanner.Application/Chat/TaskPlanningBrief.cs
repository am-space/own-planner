using System.Text.Json;
using System.Text.Json.Serialization;

namespace OwnPlanner.Application.Chat;

/// <summary>Execution retains the original delegation contract; Proposal prohibits all mutations.</summary>
public enum TaskPlanningBehavior { Execution, Proposal }

/// <summary>An entity reference supplies context, never authority or a tenant/scope override.</summary>
public sealed record TaskPlanningEntityReference(string Kind, Guid Id, string? Label = null);

/// <summary>Selected context for a fresh specialist session, excluding full conversation history.</summary>
public sealed record TaskPlanningBrief(
	IReadOnlyList<string>? Constraints = null,
	IReadOnlyList<TaskPlanningEntityReference>? EntityReferences = null,
	IReadOnlyList<string>? UserDecisions = null);

/// <summary>A suggested step, kept separate from execution-confirmed actions and never auto-applied.</summary>
public sealed record TaskPlanningProposedStep(string Description, Guid? TaskId = null, Guid? TaskListId = null);

/// <summary>Validates the chat-local delegation contract before any planner access.</summary>
public static class TaskPlanningRequestParser
{
	private static readonly JsonSerializerOptions BriefOptions = new(JsonSerializerDefaults.Web)
	{
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public static TaskPlanningAgentRequest Parse(IReadOnlyDictionary<string, object?>? arguments)
	{
		try
		{
			var root = JsonSerializer.SerializeToElement(arguments);
			var objective = root.GetProperty("objective").GetString();
			var behavior = root.TryGetProperty("behavior", out var value) ? value.GetString() switch
			{
				"execution" => TaskPlanningBehavior.Execution,
				"proposal" => TaskPlanningBehavior.Proposal,
				_ => throw new InvalidOperationException()
			} : TaskPlanningBehavior.Execution;
			var brief = root.TryGetProperty("brief", out value) ? value.Deserialize<TaskPlanningBrief>(BriefOptions) : null;
			var request = new TaskPlanningAgentRequest(objective!, OptionalGuid(root, "contextId"), OptionalGuid(root, "taskListId"), behavior, brief);
			Validate(request);
			return request;
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
		{
			throw new InvalidOperationException("Invalid task-planning request. Supply an objective (1–2000 characters), behavior 'execution' or 'proposal', optional UUID scopes, and a bounded brief: at most 8 constraints and 8 userDecisions (300 characters each), and 16 entityReferences (kind, UUID id, optional label up to 120 characters).");
		}
	}

	public static void Validate(TaskPlanningAgentRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Objective) || request.Objective.Length > 2000 || !Enum.IsDefined(request.Behavior))
			throw new InvalidOperationException("Invalid task-planning objective or behavior.");
		ValidateStrings(request.Brief?.Constraints);
		ValidateStrings(request.Brief?.UserDecisions);
		if (request.Brief?.EntityReferences is { } references &&
			(references.Count > 16 || references.Any(reference => reference is null || reference.Id == Guid.Empty ||
				reference.Kind is not ("task" or "taskList" or "goal" or "context") || reference.Label?.Length > 120)))
			throw new InvalidOperationException("Invalid or oversized task-planning entity references.");
	}

	private static void ValidateStrings(IReadOnlyList<string>? values)
	{
		if (values is not null && (values.Count > 8 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 300)))
			throw new InvalidOperationException("Invalid or oversized task-planning brief entries.");
	}

	private static Guid? OptionalGuid(JsonElement root, string name) =>
		!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ||
		(value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())) ? null : value.GetGuid();
}
