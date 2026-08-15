using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public class TaskPlanningMcpAdapterTests
{
	[Fact]
	public async Task ListToolDetailsAsync_ExposesOnlyTrustedTools()
	{
		var inner = new FakeMcpAdapter(
			"taskitem_create", "taskitem_complete", "taskitem_delete", "search_agent_call", "task_planning_agent_call");
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);

		var tools = await adapter.ListToolDetailsAsync(TestContext.Current.CancellationToken);

		tools.Select(tool => tool.Name).Should().Equal("taskitem_create");
	}

	[Theory]
	[InlineData("taskitem_complete")]
	[InlineData("taskitem_delete")]
	[InlineData("task_planning_agent_call")]
	[InlineData("search_agent_call")]
	public async Task CallToolAsync_RejectsDisallowedAndRecursiveTools(string toolName)
	{
		var inner = new FakeMcpAdapter(toolName);
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);

		var act = () => adapter.CallToolAsync(toolName, cancellationToken: TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not allowed*");
		inner.Calls.Should().BeEmpty();
	}

	[Fact]
	public async Task CreateAsync_RejectsTaskListOutsideSuppliedContextBeforeWrites()
	{
		var contextId = Guid.NewGuid();
		var otherContextId = Guid.NewGuid();
		var taskListId = Guid.NewGuid();
		var inner = new FakeMcpAdapter("context_get", "tasklist_get");
		inner.Results["context_get"] = JsonSerializer.Serialize(new { id = contextId });
		inner.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = taskListId, contextId = otherContextId });

		var act = () => TaskPlanningMcpAdapter.CreateAsync(inner, contextId, taskListId, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong*");
		inner.Calls.Should().OnlyContain(call => call.ToolName == "context_get" || call.ToolName == "tasklist_get");
	}

	[Fact]
	public async Task CallToolAsync_TaskListScope_ForcesTaskQueriesIntoThatList()
	{
		var taskListId = Guid.NewGuid();
		var inner = new FakeMcpAdapter("tasklist_get", "taskitem_list_items");
		inner.Results["tasklist_get"] = JsonSerializer.Serialize(new { id = taskListId, contextId = Guid.NewGuid() });
		inner.Results["taskitem_list_items"] = "[]";
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, taskListId, TestContext.Current.CancellationToken);

		await adapter.CallToolAsync("taskitem_list_items", new Dictionary<string, object?>(), TestContext.Current.CancellationToken);

		var call = inner.Calls.Last();
		call.ToolName.Should().Be("taskitem_list_items");
		call.Arguments!["taskListId"].Should().Be(taskListId);
	}

	[Fact]
	public async Task CallToolAsync_ContextScope_RejectsMutationIntoAnotherContext()
	{
		var contextId = Guid.NewGuid();
		var inner = new FakeMcpAdapter("context_get", "tasklist_create");
		inner.Results["context_get"] = JsonSerializer.Serialize(new { id = contextId });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, contextId, null, TestContext.Current.CancellationToken);

		var act = () => adapter.CallToolAsync("tasklist_create", new Dictionary<string, object?>
		{
			["contextId"] = Guid.NewGuid(),
			["title"] = "Outside"
		}, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the delegated scope*");
		inner.Calls.Should().ContainSingle(call => call.ToolName == "context_get");
	}

	[Fact]
	public async Task CallToolAsync_RecordsWriteOutcome()
	{
		var inner = new FakeMcpAdapter("taskitem_create");
		inner.Results["taskitem_create"] = JsonSerializer.Serialize(new { id = Guid.NewGuid() });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);

		await adapter.CallToolAsync("taskitem_create", new Dictionary<string, object?>(), TestContext.Current.CancellationToken);

		adapter.Actions.Should().ContainSingle().Which.ToolName.Should().Be("taskitem_create");
		adapter.Actions.Single().Result.Should().Contain("id");
	}

	[Fact]
	public async Task CallToolAsync_FailedWriteIsWarningNotPerformedAction()
	{
		var inner = new FakeMcpAdapter("taskitem_create");
		inner.Results["taskitem_create"] = JsonSerializer.Serialize(new { error = "Task list not found" });
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);

		await adapter.CallToolAsync("taskitem_create", new Dictionary<string, object?>(), TestContext.Current.CancellationToken);

		adapter.Actions.Should().BeEmpty();
		adapter.Warnings.Should().ContainSingle().Which.Should().Contain("Task list not found");
	}

	[Fact]
	public async Task CallToolAsync_PropagatesCancellationToAuthenticatedAdapter()
	{
		var inner = new FakeMcpAdapter("taskitem_create");
		var adapter = await TaskPlanningMcpAdapter.CreateAsync(inner, null, null, TestContext.Current.CancellationToken);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var act = () => adapter.CallToolAsync("taskitem_create", cancellationToken: cancellation.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
		adapter.Actions.Should().BeEmpty();
	}

	private sealed class FakeMcpAdapter(params string[] tools) : IMcpAdapter
	{
		private readonly IReadOnlyList<McpToolDefinition> _tools = tools.Select(name =>
			new McpToolDefinition(name, name, JsonSerializer.SerializeToElement(new { type = "object" }))).ToList();

		public Dictionary<string, string> Results { get; } = [];
		public List<(string ToolName, IReadOnlyDictionary<string, object?>? Arguments)> Calls { get; } = [];

		public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_tools);

		public Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Calls.Add((toolName, arguments));
			return Task.FromResult(Results.GetValueOrDefault(toolName, "{}"));
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
