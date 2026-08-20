using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed class TaskPlanningAgentOrchestratorTests
{
	[Fact]
	public async Task ExecuteAsync_CompletesAfterExactlyFinalAllowedToolRound_AndAccountsUsage()
	{
		var inner = new FakeMcpAdapter("taskitem_create");
		inner.Results["taskitem_create"] = JsonSerializer.Serialize(new { id = Guid.NewGuid(), title = "Plan launch" });
		var tools = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);
		var session = new FakeSession(
			new DelegatedAgentResponse("", [new DelegatedAgentToolCall("taskitem_create", new Dictionary<string, object?>())], 2, 3),
			FinalResponse("Created the launch plan", 5, 7));

		var execution = await TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Plan the launch"), tools, session, 1, TestContext.Current.CancellationToken);

		execution.Result.Status.Should().Be("completed");
		execution.Result.Actions.Should().ContainSingle().Which.ToolName.Should().Be("taskitem_create");
		execution.InputTokens.Should().Be(7);
		execution.OutputTokens.Should().Be(10);
		session.ToolResults.Should().ContainSingle().Which.Single().Succeeded.Should().BeTrue();
	}

	[Fact]
	public async Task ExecuteAsync_ReturnsLimitOnlyWhenAnotherToolCallRemainsAfterLimit()
	{
		var inner = new FakeMcpAdapter("datetime_get_current");
		var tools = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);
		var call = new DelegatedAgentToolCall("datetime_get_current", null);
		var session = new FakeSession(
			new DelegatedAgentResponse("", [call]),
			new DelegatedAgentResponse("", [call]));

		var execution = await TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Plan"), tools, session, 1, TestContext.Current.CancellationToken);

		execution.Result.Status.Should().Be("limit_reached");
		execution.Result.Warnings.Should().Contain(warning => warning.Contains("limit of 1", StringComparison.Ordinal));
		inner.Calls.Should().ContainSingle();
	}

	[Fact]
	public async Task ExecuteAsync_DisallowedToolIsWarningAndSessionContinues()
	{
		var inner = new FakeMcpAdapter("taskitem_reopen");
		var tools = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);
		var session = new FakeSession(
			new DelegatedAgentResponse("", [new DelegatedAgentToolCall("taskitem_reopen", null)]),
			FinalResponse("Could not make the prohibited change"));

		var execution = await TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Delete it"), tools, session, 2, TestContext.Current.CancellationToken);

		execution.Result.Status.Should().Be("completed");
		execution.Result.Actions.Should().BeEmpty();
		execution.Result.Warnings.Should().Contain(warning => warning.Contains("not allowed", StringComparison.Ordinal));
		session.ToolResults.Single().Single().Succeeded.Should().BeFalse();
	}

	[Fact]
	public async Task ExecuteAsync_ModelFailureReturnsSafeResult()
	{
		var tools = await TaskPlanningMcpAdapter.CreateAsync(new FakeMcpAdapter(), null, null, TestContext.Current.CancellationToken);
		var session = new FakeSession(new InvalidOperationException("provider details"));

		var execution = await TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Plan"), tools, session, 2, TestContext.Current.CancellationToken);

		execution.Result.Status.Should().Be("failed");
		execution.Result.Summary.Should().NotContain("provider details");
	}

	[Fact]
	public async Task ExecuteAsync_AmbiguousTargetIsReturnedAsUnresolvedQuestionWithoutMutation()
	{
		var tools = await TaskPlanningMcpAdapter.CreateAsync(new FakeMcpAdapter("taskitem_complete"), null, null, TestContext.Current.CancellationToken);
		var question = "Which of the two tasks named Review should I complete?";
		var session = new FakeSession(new DelegatedAgentResponse(
			JsonSerializer.Serialize(new { summary = "No task was changed.", warnings = Array.Empty<string>(), unresolvedQuestions = new[] { question } }),
			[]));

		var execution = await TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Complete Review"), tools, session, 2, TestContext.Current.CancellationToken);

		execution.Result.Actions.Should().BeEmpty();
		execution.Result.UnresolvedQuestions.Should().ContainSingle().Which.Should().Be(question);
	}

	[Fact]
	public async Task ExecuteAsync_PropagatesCancellation()
	{
		var tools = await TaskPlanningMcpAdapter.CreateAsync(new FakeMcpAdapter(), null, null, TestContext.Current.CancellationToken);
		var session = new FakeSession(FinalResponse("unused"));
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var act = () => TaskPlanningAgentOrchestrator.ExecuteAsync(
			new TaskPlanningAgentRequest("Plan"), tools, session, 2, cancellation.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact]
	public void RecoverChatSession_PreservesRestrictedModeTools()
	{
		var adapter = new ChatServiceAdapter("AIza" + new string('x', 35), "test-model");
		adapter.ResetChatSession("restricted", ["search_agent_call"]);

		adapter.RecoverChatSession();

		adapter.GetActiveFunctionNames().Should().Equal("search_agent_call");
	}

	[Fact]
	public void TaskPlanningPolicy_IsConfiguredAsTrustedSystemInstruction()
	{
		ChatServiceAdapter.TaskPlanningAgentSystemInstruction.Should().Contain("complete or move an active task to recoverable Trash only when the objective explicitly requests");
		ChatServiceAdapter.TaskPlanningAgentSystemInstruction.Should().Contain("Never reopen or restore tasks, permanently delete tasks, delete or archive task lists");
		ChatServiceAdapter.TaskPlanningAgentSystemInstruction.Should().Contain("target is ambiguous, return an unresolved question");
	}

	private static DelegatedAgentResponse FinalResponse(string summary, long inputTokens = 0, long outputTokens = 0) =>
		new(JsonSerializer.Serialize(new { summary, warnings = Array.Empty<string>(), unresolvedQuestions = Array.Empty<string>() }), [], inputTokens, outputTokens);

	private sealed class FakeSession : IDelegatedAgentSession
	{
		private readonly Queue<object> _responses;

		public FakeSession(params object[] responses) => _responses = new Queue<object>(responses);
		public List<IReadOnlyList<DelegatedAgentToolResult>> ToolResults { get; } = [];

		public Task<DelegatedAgentResponse> SendObjectiveAsync(string objective, CancellationToken cancellationToken) => Next(cancellationToken);

		public Task<DelegatedAgentResponse> SendToolResultsAsync(IReadOnlyList<DelegatedAgentToolResult> results, CancellationToken cancellationToken)
		{
			ToolResults.Add(results);
			return Next(cancellationToken);
		}

		private Task<DelegatedAgentResponse> Next(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var next = _responses.Dequeue();
			return next is Exception exception
				? Task.FromException<DelegatedAgentResponse>(exception)
				: Task.FromResult((DelegatedAgentResponse)next);
		}
	}

	private sealed class FakeMcpAdapter(params string[] toolNames) : IMcpAdapter
	{
		private readonly IReadOnlyList<McpToolDefinition> _definitions = toolNames.Select(name =>
			new McpToolDefinition(name, name, JsonSerializer.SerializeToElement(new { type = "object" }))).ToList();

		public Dictionary<string, string> Results { get; } = [];
		public List<string> Calls { get; } = [];
		public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_definitions);

		public Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Calls.Add(toolName);
			return Task.FromResult(Results.GetValueOrDefault(toolName, "{}"));
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
