using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed class ChatSkillOrchestrationTests
{
	[Fact]
	public async Task Load_UpdatesActualProviderDeclarationsAndInstructions_ThenResetsOnNextMessage()
	{
		using var provider = new ScriptedProvider(
			Calls(Call("skill_load", new { skillId = "notes" })),
			Calls(Call("skill_load", new { skillId = "notes" }), Call("noteitem_get", new { id = "note" })),
			Final("Read the note."),
			Calls(Call("noteitem_get", new { id = "note" })),
			Final("Load the skill again."));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);

		await adapter.GetResponse("Read my note", TestContext.Current.CancellationToken);
		mcp.Calls.Should().Equal("noteitem_get");
		provider.Requests[0].GetProperty("systemInstruction").ToString().Should().Contain("notes:").And.NotContain("Retrieve only the notes needed");
		Names(provider.Requests[0]).Should().BeEquivalentTo(ModeConfig.All[PlanningMode.General].InitialTools!);
		Names(provider.Requests[1]).Should().Contain("noteitem_get");
		var instructions = provider.Requests[2].GetProperty("systemInstruction").ToString();
		instructions.Split("Retrieve only the notes needed").Should().HaveCount(2);
		Names(provider.Requests[2]).Should().OnlyHaveUniqueItems();
		provider.Requests[2].GetProperty("contents").ToString().Should().NotContain("Retrieve only the notes needed");

		await adapter.GetResponse("And now?", TestContext.Current.CancellationToken);
		Names(provider.Requests[3]).Should().NotContain("noteitem_get");
		provider.Requests[3].ToString().Should().NotContain("Retrieve only the notes needed");
		provider.Requests[4].ToString().Should().Contain("not permitted for this model request");
		mcp.Calls.Should().Equal("noteitem_get");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task LoadAndToolInSameBatch_DeniesPrematureExecutionInEitherOrder(bool loadFirst)
	{
		var load = Call("skill_load", new { skillId = "notes" });
		var read = Call("noteitem_get", new { id = "note" });
		using var provider = new ScriptedProvider(
			Calls(loadFirst ? [load, read] : [read, load]), Calls(read), Final("Done"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);

		await adapter.GetResponse("Read a note", TestContext.Current.CancellationToken);

		mcp.Calls.Should().Equal("noteitem_get");
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
		Names(provider.Requests[1]).Should().Contain("noteitem_get");
	}

	[Fact]
	public async Task BothAgents_CanExecuteBeforeAnySkillLoad_AndNestedUsageIsCounted()
	{
		using var provider = new ScriptedProvider(
			Calls(Call("search_agent_call", new { query = "Find options" }), Call("task_planning_agent_call", new { objective = "Create a task" })),
			Final("Found options"),
			Calls(Call("taskitem_create", new { taskListId = Guid.NewGuid(), title = "Research options" })),
			Final("{\"summary\":\"Created a task\",\"warnings\":[],\"unresolvedQuestions\":[]}"),
			Final("Done"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);

		var result = await adapter.GetResponse("Research and create a task", TestContext.Current.CancellationToken);

		mcp.Calls.Should().Equal("taskitem_create");
		Names(provider.Requests[0]).Should().Contain(["search_agent_call", "task_planning_agent_call"]);
		provider.Requests[1].ToString().Should().NotContain("Skills available via");
		provider.Requests[2].ToString().Should().NotContain("Skills available via");
		result.InputTokens.Should().Be(10);
		result.OutputTokens.Should().Be(15);
	}

	[Theory]
	[InlineData(PlanningMode.SystemAnalysis, "goal_delete")]
	[InlineData(PlanningMode.SystemAnalysis, "task_planning_agent_call")]
	[InlineData(PlanningMode.DayWork, "search_agent_call")]
	[InlineData(PlanningMode.DayWork, "skill_load")]
	public async Task ExistingModes_DenyUndeclaredCallsAtExecution(PlanningMode mode, string name)
	{
		using var provider = new ScriptedProvider(Calls(Call(name, new { skillId = "notes" })), Final("Denied"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, mode);

		await adapter.GetResponse("Try it", TestContext.Current.CancellationToken);

		mcp.Calls.Should().BeEmpty();
		provider.Requests.Should().HaveCount(2);
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
	}

	[Fact]
	public async Task EmptyDeclarationBaseline_FailsClosedAtExecution()
	{
		using var provider = new ScriptedProvider(Calls(Call("noteitem_create", new { title = "Denied" })), Final("Denied"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);
		adapter.ResetChatSession("No tools", []);

		await adapter.GetResponse("Try it", TestContext.Current.CancellationToken);

		provider.Requests[0].ToString().Should().NotContain("functionDeclarations");
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
		mcp.Calls.Should().BeEmpty();
	}

	[Fact]
	public async Task FailedLoads_AreBounded_AndDoNotFetchOrActivatePartialTools()
	{
		using var provider = new ScriptedProvider(
			Calls(Call("skill_load", new { skillId = new string('x', 10_000) }), Call("skill_load", new { skillId = "notes" })),
			Final("Unavailable"));
		var mcp = new RecordingMcpAdapter("noteitem_get");
		await using var adapter = CreateAdapter(provider, mcp);

		await adapter.GetResponse("Load skills", TestContext.Current.CancellationToken);

		mcp.Calls.Should().BeEmpty();
		Names(provider.Requests[1]).Should().NotContain("noteitem_create");
		provider.Requests[1].ToString().Should().Contain("Unknown skill").And.Contain("required planner tools are not configured");
		var responses = provider.Requests[1].GetProperty("contents").EnumerateArray()
			.SelectMany(content => content.GetProperty("parts").EnumerateArray())
			.Where(part => part.TryGetProperty("functionResponse", out _)).ToList();
		responses.Should().HaveCount(2).And.OnlyContain(part => part.ToString().Length < 500);
	}

	[Fact]
	public async Task RepeatedLoads_ConsumeTheExistingRoundBudget()
	{
		var load = Calls(Call("skill_load", new { skillId = "notes" }));
		using var provider = new ScriptedProvider(load, load, load);
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, maxRounds: 2);

		await adapter.GetResponse("Load forever", TestContext.Current.CancellationToken);

		provider.Requests.Should().HaveCount(3);
		mcp.Calls.Should().BeEmpty();
	}

	[Fact]
	public async Task CancellationAfterLoading_DoesNotLeakSkillsIntoTheNextTurn()
	{
		using var cancellation = new CancellationTokenSource();
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "notes" })), Final("Cancelled"), Final("Next"));
		provider.OnRequest = count => { if (count == 2) cancellation.Cancel(); };
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp);

		var act = () => adapter.GetResponse("Load", cancellation.Token);
		await act.Should().ThrowAsync<OperationCanceledException>();
		provider.OnRequest = null;
		await adapter.GetResponse("Next", TestContext.Current.CancellationToken);

		Names(provider.Requests[^1]).Should().NotContain("noteitem_get");
		provider.Requests[^1].ToString().Should().NotContain("Retrieve only the notes needed");
		mcp.Calls.Should().BeEmpty();
	}

	[Fact]
	public async Task SeparateConversations_RebuildRecoveryAndModeSwitch_DoNotRetainLoadedSkills()
	{
		using var provider = new ScriptedProvider(Calls(Call("skill_load", new { skillId = "notes" })), Final("Loaded"), Final("Other conversation"), Final("Rebuilt"), Final("Recovered"), Final("Switched"));
		await using var first = CreateAdapter(provider, new RecordingMcpAdapter());
		await using var second = CreateAdapter(provider, new RecordingMcpAdapter());
		await first.GetResponse("Load", TestContext.Current.CancellationToken);
		await second.GetResponse("Hello", TestContext.Current.CancellationToken);
		first.RebuildSession("General", ModeConfig.All[PlanningMode.General].InitialTools, [new ChatMessage(ChatRole.User, "Earlier request")]);
		await first.GetResponse("Continue", TestContext.Current.CancellationToken);
		first.RecoverChatSession();
		await first.GetResponse("Continue", TestContext.Current.CancellationToken);
		var day = ModeConfig.All[PlanningMode.DayWork];
		first.ConfigureToolPolicy(ChatToolPolicy.ForMode(day));
		first.ResetChatSession(day.SystemPrompt, day.AllowedTools);
		await first.GetResponse("Today", TestContext.Current.CancellationToken);

		foreach (var request in provider.Requests.Skip(2))
		{
			Names(request).Should().NotContain("noteitem_get");
			request.ToString().Should().NotContain("Retrieve only the notes needed");
		}
		Names(provider.Requests[3]).Should().Contain("skill_load");
		Names(provider.Requests[4]).Should().Contain("skill_load");
		Names(provider.Requests[5]).Should().NotContain("skill_load");
	}

	private static ChatServiceAdapter CreateAdapter(ScriptedProvider provider, RecordingMcpAdapter mcp, PlanningMode mode = PlanningMode.General, int maxRounds = 10)
	{
		var adapter = new ChatServiceAdapter("AIza" + new string('x', 35), "test-model", maxRounds, mcp, httpClientFactory: provider);
		var config = ModeConfig.All[mode];
		adapter.ConfigureToolPolicy(ChatToolPolicy.ForMode(config));
		adapter.ResetChatSession(config.SystemPrompt, config.InitialTools ?? config.AllowedTools);
		return adapter;
	}

	private static string[] Names(JsonElement request) => request.GetProperty("tools").EnumerateArray()
		.Where(tool => tool.TryGetProperty("functionDeclarations", out _))
		.SelectMany(tool => tool.GetProperty("functionDeclarations").EnumerateArray())
		.Select(declaration => declaration.GetProperty("name").GetString()!).ToArray();
	private static object Call(string name, object args) => new { functionCall = new { name, args } };
	private static string Calls(params object[] parts) => Response(parts);
	private static string Final(string text) => Response([new { text }]);
	private static string Response(object[] parts) => JsonSerializer.Serialize(new
	{
		candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } },
		usageMetadata = new { promptTokenCount = 2, candidatesTokenCount = 3, totalTokenCount = 5 }
	});

	private sealed class ScriptedProvider(params string[] responses) : HttpMessageHandler, IHttpClientFactory
	{
		private readonly Queue<string> _responses = new(responses);
		public List<JsonElement> Requests { get; } = [];
		public Action<int>? OnRequest { get; set; }
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var json = await request.Content!.ReadAsStringAsync(cancellationToken);
			Requests.Add(JsonSerializer.Deserialize<JsonElement>(json));
			var response = _responses.Dequeue();
			OnRequest?.Invoke(Requests.Count);
			cancellationToken.ThrowIfCancellationRequested();
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
		}
	}

	private sealed class RecordingMcpAdapter(string? missingTool = null) : IMcpAdapter
	{
		public List<string> Calls { get; } = [];
		public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<McpToolDefinition>>(ModeConfig.All.Values.SelectMany(config => config.AllowedTools)
				.Concat(TaskPlanningMcpAdapter.ReadTools).Concat(TaskPlanningMcpAdapter.WriteTools)
				.Where(name => name != missingTool && name is not "skill_load" and not "search_agent_call" and not "task_planning_agent_call")
				.Distinct().Select(name => new McpToolDefinition(name, name, JsonSerializer.SerializeToElement(new { type = "object" }))).ToArray());
		public Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Calls.Add(toolName);
			return Task.FromResult("{}");
		}
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
