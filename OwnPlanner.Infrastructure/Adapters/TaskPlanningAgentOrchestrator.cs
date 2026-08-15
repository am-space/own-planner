using System.Text.Json;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Adapters;

internal sealed record DelegatedAgentToolCall(string Name, IReadOnlyDictionary<string, object?>? Arguments);
internal sealed record DelegatedAgentToolResult(string Name, bool Succeeded, string Payload);
internal sealed record DelegatedAgentResponse(
	string Text,
	IReadOnlyList<DelegatedAgentToolCall> ToolCalls,
	long InputTokens = 0,
	long OutputTokens = 0);

internal interface IDelegatedAgentSession
{
	Task<DelegatedAgentResponse> SendObjectiveAsync(string objective, CancellationToken cancellationToken);
	Task<DelegatedAgentResponse> SendToolResultsAsync(IReadOnlyList<DelegatedAgentToolResult> results, CancellationToken cancellationToken);
}

internal sealed record TaskPlanningAgentExecution(TaskPlanningAgentResult Result, long InputTokens, long OutputTokens);

internal static class TaskPlanningAgentOrchestrator
{
	internal static async Task<TaskPlanningAgentExecution> ExecuteAsync(
		TaskPlanningAgentRequest request,
		TaskPlanningMcpAdapter tools,
		IDelegatedAgentSession session,
		int maxToolCallRounds,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(tools);
		ArgumentNullException.ThrowIfNull(session);
		if (maxToolCallRounds <= 0) throw new ArgumentOutOfRangeException(nameof(maxToolCallRounds));

		long inputTokens = 0;
		long outputTokens = 0;
		var warnings = new List<string>();

		try
		{
			var scope = $"Context scope: {request.ContextId?.ToString() ?? "none"}; task-list scope: {request.TaskListId?.ToString() ?? "none"}.";
			var response = await session.SendObjectiveAsync($"Objective: {request.Objective}\n{scope}", cancellationToken).ConfigureAwait(false);
			AddUsage(response, ref inputTokens, ref outputTokens);
			var completedRounds = 0;

			while (true)
			{
				if (response.ToolCalls.Count == 0)
				{
					return new TaskPlanningAgentExecution(
						BuildResult("completed", response.Text, tools.Actions, warnings.Concat(tools.Warnings).ToList()),
						inputTokens,
						outputTokens);
				}

				if (completedRounds >= maxToolCallRounds)
				{
					warnings.Add($"Delegation reached the configured limit of {maxToolCallRounds} tool-call rounds.");
					return new TaskPlanningAgentExecution(
						BuildResult("limit_reached", response.Text, tools.Actions, warnings.Concat(tools.Warnings).ToList()),
						inputTokens,
						outputTokens);
				}

				var results = new List<DelegatedAgentToolResult>();
				foreach (var call in response.ToolCalls)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						var result = await tools.CallToolAsync(call.Name, call.Arguments, cancellationToken).ConfigureAwait(false);
						results.Add(new DelegatedAgentToolResult(call.Name, true, result));
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						warnings.Add($"{call.Name}: {ex.Message}");
						results.Add(new DelegatedAgentToolResult(call.Name, false, ex.Message));
					}
				}

				response = await session.SendToolResultsAsync(results, cancellationToken).ConfigureAwait(false);
				AddUsage(response, ref inputTokens, ref outputTokens);
				completedRounds++;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			warnings.Add("The delegated planning session failed before it could finish.");
			return new TaskPlanningAgentExecution(
				new TaskPlanningAgentResult(
					"failed",
					"The delegated planning session could not finish safely.",
					tools.Actions,
					warnings.Concat(tools.Warnings).ToList(),
					[]),
				inputTokens,
				outputTokens);
		}
	}

	private static void AddUsage(DelegatedAgentResponse response, ref long inputTokens, ref long outputTokens)
	{
		inputTokens += response.InputTokens;
		outputTokens += response.OutputTokens;
	}

	private static TaskPlanningAgentResult BuildResult(
		string status,
		string modelText,
		IReadOnlyList<TaskPlanningAgentAction> actions,
		IReadOnlyList<string> executionWarnings)
	{
		try
		{
			var json = modelText.Trim();
			if (json.StartsWith("```", StringComparison.Ordinal))
			{
				var firstNewLine = json.IndexOf('\n');
				var closingFence = json.LastIndexOf("```", StringComparison.Ordinal);
				if (firstNewLine >= 0 && closingFence > firstNewLine)
					json = json[(firstNewLine + 1)..closingFence].Trim();
			}

			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			var summary = root.TryGetProperty("summary", out var summaryProperty) ? summaryProperty.GetString() ?? string.Empty : modelText;
			var warnings = executionWarnings.Concat(ReadStringArray(root, "warnings")).Distinct(StringComparer.Ordinal).ToList();
			var questions = ReadStringArray(root, "unresolvedQuestions");
			return new TaskPlanningAgentResult(status, summary, actions, warnings, questions);
		}
		catch (JsonException)
		{
			var questions = modelText.TrimEnd().EndsWith("?", StringComparison.Ordinal) ? new[] { modelText } : [];
			return new TaskPlanningAgentResult(status, modelText, actions, executionWarnings, questions);
		}
	}

	private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName) =>
		root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
			? property.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
			: [];
}
