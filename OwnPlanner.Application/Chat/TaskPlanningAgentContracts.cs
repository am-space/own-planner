using System.Text.Json;

namespace OwnPlanner.Application.Chat;

/// <summary>Defines the objective and optional entity scope for one task-planning delegation.</summary>
public sealed record TaskPlanningAgentRequest(string Objective, Guid? ContextId = null, Guid? TaskListId = null);

/// <summary>Describes one planner mutation attempted by the delegated task-planning agent.</summary>
public sealed record TaskPlanningAgentAction(string ToolName, string Result);

/// <summary>Returns the bounded outcome of an isolated task-planning delegation to the parent planner.</summary>
public sealed record TaskPlanningAgentResult(
	string Status,
	string Summary,
	IReadOnlyList<TaskPlanningAgentAction> Actions,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<string> UnresolvedQuestions);

/// <summary>
/// Enforces the trusted task-planning tool allowlist and optional context/task-list boundary before
/// forwarding calls to the authenticated planner tool adapter.
/// </summary>
public sealed class TaskPlanningMcpAdapter : IMcpAdapter
{
	public static readonly IReadOnlySet<string> ReadTools = new HashSet<string>(StringComparer.Ordinal)
	{
		"goal_list", "goal_get", "context_list", "context_get", "tasklist_all", "tasklist_get",
		"taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get",
		"datetime_get_current"
	};

	public static readonly IReadOnlySet<string> WriteTools = new HashSet<string>(StringComparer.Ordinal)
	{
		"tasklist_create", "tasklist_update", "taskitem_create", "taskitem_update", "taskitem_assign",
		"taskitem_set_focus_date", "taskitem_set_important"
	};

	private readonly IMcpAdapter _inner;
	private readonly Guid? _contextId;
	private readonly Guid? _taskListId;
	private readonly List<TaskPlanningAgentAction> _actions = [];
	private readonly List<string> _warnings = [];

	private TaskPlanningMcpAdapter(IMcpAdapter inner, Guid? contextId, Guid? taskListId)
	{
		_inner = inner;
		_contextId = contextId;
		_taskListId = taskListId;
	}

	public IReadOnlyList<TaskPlanningAgentAction> Actions => _actions;
	public IReadOnlyList<string> Warnings => _warnings;

	public static async Task<TaskPlanningMcpAdapter> CreateAsync(
		IMcpAdapter inner,
		Guid? contextId,
		Guid? taskListId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(inner);

		if (contextId.HasValue)
		{
			var context = await inner.CallToolAsync("context_get", Args(("id", contextId.Value)), cancellationToken).ConfigureAwait(false);
			EnsureFound(context, "context", contextId.Value);
		}

		if (taskListId.HasValue)
		{
			var taskList = await inner.CallToolAsync("tasklist_get", Args(("id", taskListId.Value)), cancellationToken).ConfigureAwait(false);
			EnsureFound(taskList, "task list", taskListId.Value);
			if (contextId.HasValue && ReadGuid(taskList, "contextId") != contextId)
				throw new InvalidOperationException("The supplied task list does not belong to the supplied context.");
		}

		return new TaskPlanningMcpAdapter(inner, contextId, taskListId);
	}

	public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public async Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default)
	{
		var definitions = await _inner.ListToolDetailsAsync(cancellationToken).ConfigureAwait(false);
		return definitions.Where(definition => ReadTools.Contains(definition.Name) || WriteTools.Contains(definition.Name)).ToList();
	}

	public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
	{
		if (!ReadTools.Contains(toolName) && !WriteTools.Contains(toolName))
			throw new InvalidOperationException($"Tool '{toolName}' is not allowed for task-planning delegation.");

		var scopedArguments = arguments is null
			? new Dictionary<string, object?>(StringComparer.Ordinal)
			: new Dictionary<string, object?>(arguments, StringComparer.Ordinal);

		await EnforceScopeAsync(toolName, scopedArguments, cancellationToken).ConfigureAwait(false);
		var result = await _inner.CallToolAsync(toolName, scopedArguments, cancellationToken).ConfigureAwait(false);
		if (WriteTools.Contains(toolName))
		{
			if (HasError(result))
				_warnings.Add($"Tool '{toolName}' failed: {BoundResult(result)}");
			else
				_actions.Add(new TaskPlanningAgentAction(toolName, BoundResult(result)));
		}
		return result;
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private async Task EnforceScopeAsync(string toolName, Dictionary<string, object?> arguments, CancellationToken cancellationToken)
	{
		if (!_contextId.HasValue && !_taskListId.HasValue)
			return;

		switch (toolName)
		{
			case "goal_list":
			case "goal_get":
			case "context_list":
				throw OutOfScope(toolName, "Broad goal and context reads are unavailable while delegation is scoped.");
			case "context_get":
				if (!_contextId.HasValue || RequiredGuid(arguments, "id") != _contextId.Value)
					throw OutOfScope(toolName, "Only the explicitly scoped context can be read.");
				break;
			case "tasklist_all":
				if (_taskListId.HasValue)
					throw OutOfScope(toolName, "Use tasklist_get for the scoped task list.");
				arguments["contextId"] = _contextId;
				arguments["includeUnassigned"] = false;
				break;
			case "tasklist_get":
			case "tasklist_update":
				await EnsureTaskListInScopeAsync(RequiredGuid(arguments, "id"), cancellationToken).ConfigureAwait(false);
				if (toolName == "tasklist_update" && arguments.TryGetValue("contextId", out var destination) && destination is not null)
					EnsureContextInScope(ToGuid(destination, "contextId"));
				break;
			case "tasklist_create":
				if (_taskListId.HasValue)
					throw OutOfScope(toolName, "A new task list cannot be created inside an existing task-list scope.");
				EnsureContextInScope(RequiredGuid(arguments, "contextId"));
				break;
			case "taskitem_list_items":
				if (_taskListId.HasValue)
					arguments["taskListId"] = _taskListId;
				else if (!arguments.TryGetValue("taskListId", out var listValue) || listValue is null)
					throw OutOfScope(toolName, "A taskListId is required for task reads inside a context scope.");
				await EnsureTaskListInScopeAsync(RequiredGuid(arguments, "taskListId"), cancellationToken).ConfigureAwait(false);
				break;
			case "taskitem_create":
				await EnsureTaskListInScopeAsync(RequiredGuid(arguments, "taskListId"), cancellationToken).ConfigureAwait(false);
				break;
			case "taskitem_assign":
				await EnsureTaskInScopeAsync(RequiredGuid(arguments, "taskId"), cancellationToken).ConfigureAwait(false);
				await EnsureTaskListInScopeAsync(RequiredGuid(arguments, "taskListId"), cancellationToken).ConfigureAwait(false);
				break;
			case "taskitem_get":
			case "taskitem_update":
			case "taskitem_set_focus_date":
			case "taskitem_set_important":
				await EnsureTaskInScopeAsync(RequiredGuid(arguments, "id"), cancellationToken).ConfigureAwait(false);
				break;
			case "taskitem_list_by_goal":
			case "taskitem_list_by_focus_date":
				throw OutOfScope(toolName, "Broad task queries are unavailable while delegation is scoped.");
		}
	}

	private async Task EnsureTaskInScopeAsync(Guid taskId, CancellationToken cancellationToken)
	{
		var task = await _inner.CallToolAsync("taskitem_get", Args(("id", taskId)), cancellationToken).ConfigureAwait(false);
		EnsureFound(task, "task", taskId);
		await EnsureTaskListInScopeAsync(ReadGuid(task, "taskListId") ?? throw OutOfScope("taskitem_get", "The task has no task list."), cancellationToken).ConfigureAwait(false);
	}

	private async Task EnsureTaskListInScopeAsync(Guid taskListId, CancellationToken cancellationToken)
	{
		if (_taskListId.HasValue && taskListId != _taskListId.Value)
			throw OutOfScope("task list", "The requested task list is outside the delegated scope.");

		if (_contextId.HasValue)
		{
			var taskList = await _inner.CallToolAsync("tasklist_get", Args(("id", taskListId)), cancellationToken).ConfigureAwait(false);
			EnsureFound(taskList, "task list", taskListId);
			if (ReadGuid(taskList, "contextId") != _contextId)
				throw OutOfScope("task list", "The requested task list is outside the delegated context.");
		}
	}

	private void EnsureContextInScope(Guid contextId)
	{
		if (_contextId.HasValue && contextId != _contextId.Value)
			throw OutOfScope("context", "The requested context is outside the delegated scope.");
	}

	private static InvalidOperationException OutOfScope(string toolName, string detail) => new($"Tool '{toolName}' cannot be used outside the delegated scope. {detail}");

	private static Guid RequiredGuid(IReadOnlyDictionary<string, object?> arguments, string name) =>
		arguments.TryGetValue(name, out var value) && value is not null ? ToGuid(value, name) : throw new InvalidOperationException($"Tool argument '{name}' is required.");

	private static Guid ToGuid(object value, string name) => value switch
	{
		Guid guid => guid,
		string valueText when Guid.TryParse(valueText, out var guid) => guid,
		JsonElement { ValueKind: JsonValueKind.String } element when Guid.TryParse(element.GetString(), out var guid) => guid,
		_ => throw new InvalidOperationException($"Tool argument '{name}' must be a valid UUID.")
	};

	private static IReadOnlyDictionary<string, object?> Args(params (string Name, object? Value)[] values) => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

	private static void EnsureFound(string json, string entityName, Guid id)
	{
		if (HasError(json))
			throw new InvalidOperationException($"The supplied {entityName} '{id}' was not found in the authenticated planner.");
	}

	private static bool HasError(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out _);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static string BoundResult(string result) => result.Length <= 1_000 ? result : result[..1_000] + "… [truncated]";

	private static Guid? ReadGuid(string json, string propertyName)
	{
		using var document = JsonDocument.Parse(json);
		if (document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out var value))
			return value;
		return null;
	}
}
