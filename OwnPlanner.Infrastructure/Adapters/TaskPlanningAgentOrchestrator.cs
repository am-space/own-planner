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

/// <summary>A fresh specialist session receiving only a validated serialized brief and its own tool results.</summary>
internal interface IDelegatedAgentSession
{
	/// <summary>Sends the bounded JSON objective, behavior, scopes and selected context without parent history.</summary>
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
		TaskPlanningRequestParser.Validate(request);
		if (request.Behavior != tools.Behavior)
			throw new InvalidOperationException("Delegation behavior does not match its execution policy.");

		long inputTokens = 0;
		long outputTokens = 0;
		var warnings = new List<string>();

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			var brief = JsonSerializer.Serialize(new
			{
				request.Objective,
				Behavior = request.Behavior == TaskPlanningBehavior.Proposal ? "proposal" : "execution",
				request.ContextId,
				request.TaskListId,
				request.Brief
			}, new JsonSerializerOptions(JsonSerializerDefaults.Web));
			var response = await session.SendObjectiveAsync(brief, cancellationToken).ConfigureAwait(false);
			AddUsage(response, ref inputTokens, ref outputTokens);
			var completedRounds = 0;

			while (true)
			{
				if (response.ToolCalls.Count == 0)
				{
					return new TaskPlanningAgentExecution(
						BuildResult(request.Behavior == TaskPlanningBehavior.Proposal ? "proposed" : "completed", response.Text, tools.Actions, warnings.Concat(tools.Warnings).ToList(), request.Behavior),
						inputTokens,
						outputTokens);
				}

				if (completedRounds >= maxToolCallRounds)
				{
					warnings.Add($"Delegation reached the configured limit of {maxToolCallRounds} tool-call rounds.");
					return new TaskPlanningAgentExecution(
						BuildResult("limit_reached", response.Text, tools.Actions, warnings.Concat(tools.Warnings).ToList(), request.Behavior),
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
		IReadOnlyList<string> executionWarnings,
		TaskPlanningBehavior behavior)
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
			var hasSummary = root.TryGetProperty("summary", out var summaryProperty);
			if (behavior == TaskPlanningBehavior.Proposal && (!hasSummary || summaryProperty.ValueKind != JsonValueKind.String))
				throw new JsonException();
			var summary = hasSummary ? summaryProperty.GetString() ?? string.Empty : modelText;
			if (summary.Length > 2000) throw new JsonException();
			var warnings = executionWarnings.Concat(ReadStringArray(root, "warnings")).Distinct(StringComparer.Ordinal).ToList();
			var questions = ReadStringArray(root, "unresolvedQuestions");
			var proposedPlan = root.TryGetProperty("proposedPlan", out var planProperty)
				? planProperty.Deserialize<List<TaskPlanningProposedStep>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [] : [];
			if (proposedPlan.Count > 20 || proposedPlan.Any(step => step is null || string.IsNullOrWhiteSpace(step.Description) || step.Description.Length > 500))
				throw new JsonException();
			return new TaskPlanningAgentResult(status, summary, actions, warnings, questions) { ProposedPlan = proposedPlan };
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
		{
			var length = Math.Min(modelText.Length, 2000);
			if (length < modelText.Length && char.IsHighSurrogate(modelText[length - 1]) && char.IsLowSurrogate(modelText[length])) length--;
			var text = modelText[..length];
			var questions = text.TrimEnd().EndsWith("?", StringComparison.Ordinal) ? new[] { text } : [];
			return new TaskPlanningAgentResult(status == "proposed" ? "invalid_proposal" : status, text, actions,
				executionWarnings.Append("The specialist did not return a valid bounded structured result; no proposal steps were accepted.").ToList(), questions);
		}
	}

	private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var property)) return [];
		if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() > 8) throw new JsonException();
		var values = property.EnumerateArray().Select(item => item.GetString()).ToArray();
		if (values.Any(value => value is null || value.Length > 500)) throw new JsonException();
		return values!;
	}
}
