using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.Usage;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task LinkToolHasSameFocusedSchemaAsStdioAndRejectsForeignTaskOrGoal()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var taskA = await SeedUserTaskIdAsync("user-a", "A task", ct);
		var taskB = await SeedUserTaskIdAsync("user-b", "B task", ct);
		await using var a = CreateAdapter(services, "user-a");
		await using var b = CreateAdapter(services, "user-b");
		var goalA = ParseJsonElement(await a.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = "A goal", ["horizon"] = "Yearly" }, ct)).GetProperty("id").GetGuid();
		var goalB = ParseJsonElement(await b.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = "B goal", ["horizon"] = "Yearly" }, ct)).GetProperty("id").GetGuid();
		var schema = (await a.ListToolDetailsAsync(ct)).Single(t => t.Name == "taskitem_link_goal").JsonSchema!.Value;
		schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("taskId", "goalId");
		schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo("taskId", "goalId");
		foreach (var (task, goal) in new[] { (taskA, goalB), (taskB, goalA) })
		{
			var failed = ParseJsonElement(await a.CallToolAsync("taskitem_link_goal", new Dictionary<string, object?> { ["taskId"] = task, ["goalId"] = goal }, ct));
			failed.GetProperty("error").GetString().Should().Contain("not found");
		}
		foreach (var (adapter, id) in new[] { (a, taskA), (b, taskB) })
			ParseJsonElement(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct)).TryGetProperty("goalId", out _).Should().BeFalse();
		var linked = ParseJsonElement(await a.CallToolAsync("taskitem_link_goal", new Dictionary<string, object?> { ["taskId"] = taskA, ["goalId"] = goalA }, ct));
		linked.GetProperty("id").GetGuid().Should().Be(taskA);
		linked.GetProperty("goalId").GetGuid().Should().Be(goalA);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task WebAndTelegramCreateUnlinkedThenApplyConfirmationUsingSameTenantTools(bool telegram)
	{
		var ct = TestContext.Current.CancellationToken;
		var userA = Guid.NewGuid(); var userB = Guid.NewGuid();
		await using var services = BuildTenantServiceProvider();
		var listA = await SeedUserTaskAsync(userA.ToString(), "A existing task", ct);
		await SeedUserTaskAsync(userB.ToString(), "B private task", ct);
		await using var a = CreateAdapter(services, userA.ToString());
		await using var b = CreateAdapter(services, userB.ToString());
		var goalA = ParseJsonElement(await a.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = "Run a half marathon", ["horizon"] = "Quarterly", ["targetPeriod"] = "2026-Q4" }, ct)).GetProperty("id").GetGuid();
		await b.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = "B private goal", ["horizon"] = "Yearly" }, ct);
		foreach (var status in new[] { "Paused", "Achieved", "Dropped" })
		{
			var id = ParseJsonElement(await a.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = $"{status} training goal", ["horizon"] = "Yearly" }, ct)).GetProperty("id").GetGuid();
			await a.CallToolAsync("goal_update", new Dictionary<string, object?> { ["id"] = id, ["status"] = status }, ct);
		}
		using var provider = new GoalLinkProvider(listA, goalA);
		var factory = new ChatServiceFactory(Options.Create(new ChatSettings()), new GoalLinkChatFactory(provider),
			services.GetRequiredService<ILogger<ChatServiceFactory>>(), services.GetRequiredService<ILogger<DirectToolMcpAdapter>>(),
			services.GetRequiredService<ILogger<PlanningService>>(), services.GetRequiredService<IServiceScopeFactory>(),
			services.GetRequiredService<IPlannerSessionContextAccessor>(), services.GetRequiredService<PerUserAppInitializationService>());
		using var sessions = new ChatSessionManager(factory, services.GetRequiredService<ILogger<ChatSessionManager>>());
		var quota = Substitute.For<IUsageQuotaService>();
		quota.CheckAndReserveAsync(userA.ToString(), Arg.Any<CancellationToken>()).Returns(new UsageStatus(200, 1, 199, DateTimeOffset.UtcNow.AddDays(1)));
		var bot = Substitute.For<ITelegramBotClient>();
		var integration = Substitute.For<ITelegramIntegrationService>();
		integration.ReserveUpdateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(TelegramUpdateReservation.Reserved);
		integration.FindLinkedAccountAsync(100, 200, Arg.Any<CancellationToken>()).Returns(new TelegramLinkedAccount(userA, 100, 200, PlanningMode.General));
		integration.TryAdvanceChatUpdateAsync(userA, Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
		var tg = new TelegramController(integration, bot, sessions, quota, new TelegramChatLock(), Options.Create(new TelegramOptions { Enabled = true }), services.GetRequiredService<ILogger<TelegramController>>());
		var web = new ChatController(sessions, quota, services.GetRequiredService<ILogger<ChatController>>(), Options.Create(new ChatSettings()))
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
				[new Claim(ClaimTypes.NameIdentifier, userA.ToString()), new Claim("SessionId", "web-goal-link")], "TestAuth")) } }
		};
		async Task Send(string text, long update)
		{
			if (telegram)
				await tg.Webhook(new TelegramUpdate { UpdateId = update, Message = new TelegramMessage { Text = text, From = new TelegramUser { Id = 100 }, Chat = new TelegramChat { Id = 200, Type = "private" } } }, ct);
			else
				(await web.SendMessage(new ChatRequest { Message = text }, ct)).Should().BeOfType<OkObjectResult>();
		}
		await Send("Add Book physiotherapy", 42);
		provider.TaskId.Should().NotBeEmpty();
		var before = ParseJsonElement(await a.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = provider.TaskId }, ct));
		before.TryGetProperty("goalId", out _).Should().BeFalse();
		provider.LastRequest.Should().Contain("Run a half marathon").And.NotContain("B private goal").And.NotContain("Paused training goal").And.NotContain("Achieved training goal").And.NotContain("Dropped training goal");
		await Send("Yes, link it", 43);
		var after = ParseJsonElement(await a.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = provider.TaskId }, ct));
		after.GetProperty("goalId").GetGuid().Should().Be(goalA);
		after.GetProperty("title").GetString().Should().Be("Book physiotherapy");
		var foreign = ParseJsonElement(await b.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = provider.TaskId }, ct));
		foreign.GetProperty("error").GetString().Should().Be("Task not found");
		if (telegram)
		{
			await bot.Received(1).SendTextAsync(200, "Added Book physiotherapy. Link it to Run a half marathon?", Arg.Any<CancellationToken>());
			await bot.Received(1).SendTextAsync(200, "Linked to Run a half marathon.", Arg.Any<CancellationToken>());
		}
	}

	private sealed class GoalLinkChatFactory(GoalLinkProvider provider) : IChatAdapterFactory
	{
		public IChatAdapter Create(IMcpAdapter? mcpAdapter) => new ChatServiceAdapter("AIza" + new string('x', 35), "test-model", mcpAdapter: mcpAdapter, httpClientFactory: provider);
	}

	private sealed class GoalLinkProvider(Guid list, Guid goal) : HttpMessageHandler, IHttpClientFactory
	{
		private int _round;
		public Guid TaskId { get; private set; }
		public string LastRequest { get; private set; } = "";
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
			using var document = JsonDocument.Parse(LastRequest);
			foreach (var part in document.RootElement.GetProperty("contents").EnumerateArray().SelectMany(c => c.GetProperty("parts").EnumerateArray()))
				if (part.TryGetProperty("functionResponse", out var response) && response.GetProperty("name").GetString() == "taskitem_create")
				{
					using var result = JsonDocument.Parse(response.GetProperty("response").GetProperty("result").GetString()!);
					TaskId = result.RootElement.GetProperty("id").GetGuid();
				}
			object[] parts = _round++ switch
			{
				0 or 4 => [new { functionCall = new { name = "skill_load", args = new { skillId = "task_management" } } }],
				1 => [new { functionCall = new { name = "taskitem_create", args = new { title = "Book physiotherapy", taskListId = list } } }],
				2 => [new { functionCall = new { name = "goal_list", args = new { includeInactive = false } } }],
				3 => [new { text = "Added Book physiotherapy. Link it to Run a half marathon?" }],
				5 => [new { functionCall = new { name = "taskitem_link_goal", args = new { taskId = TaskId, goalId = goal } } }],
				_ => [new { text = "Linked to Run a half marathon." }]
			};
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
			{
				candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } }
			}), Encoding.UTF8, "application/json") };
		}
	}
}
