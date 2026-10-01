using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Users;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Telegram;
using OwnPlanner.Infrastructure.WeeklyReviews;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task GoalReviewAndLinkActionStayWithinAuthenticatedUserDatabase()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var taskA = await SeedUserTaskIdAsync("user-a", "A task", ct);
		await SeedUserTaskIdAsync("user-b", "B task", ct);
		var goalIds = new List<Guid>();
		foreach (var (userId, title) in new[] { ("user-a", "A private goal"), ("user-b", "B private goal") })
		{
			var path = Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db");
			await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
			var goal = new Goal(title, GoalHorizon.Yearly, targetPeriod: "2026");
			db.Goals.Add(goal);
			await db.SaveChangesAsync(ct);
			goalIds.Add(goal.Id);
		}
		await using var a = CreateAdapter(services, "user-a");
		await using var b = CreateAdapter(services, "user-b");
		foreach (var adapter in new[] { a, b })
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = true, ["timeZoneId"] = "UTC" }, ct);
		await a.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = taskA, ["dueAt"] = TenantTestUtcNow.AddDays(-1).ToString("O") }, ct);
		var view = ParseJsonElement(await a.CallToolAsync("weekly_review_open", cancellationToken: ct));
		view.ToString().Should().Contain("A private goal").And.NotContain("B private goal");
		var bView = ParseJsonElement(await b.CallToolAsync("weekly_review_open", cancellationToken: ct));
		bView.ToString().Should().Contain("B private goal").And.NotContain("A private goal");
		var row = view.GetProperty("report").GetProperty("tasks")[0];
		var result = ParseJsonElement(await a.CallToolAsync("weekly_review_apply", new Dictionary<string, object?>
		{
			["reviewId"] = view.GetProperty("review").GetProperty("id").GetGuid(),
			["taskId"] = taskA, ["revision"] = row.GetProperty("revision").GetString(),
			["action"] = "linkGoal", ["goalId"] = goalIds[1].ToString()
		}, ct));
		result.GetProperty("applied").GetBoolean().Should().BeFalse();
		await using var verify = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={Path.Combine(_tempDirectory, "ownplanner-user-user-a.db")}").Options);
		(await verify.TaskItems.FindAsync([taskA], ct))!.GoalId.Should().BeNull();
	}

	[Fact]
	public async Task PauseResumeAndMonthlyMentionsAreSharedAcrossChatAndTelegramButIsolatedBetweenUsers()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var userA = Guid.NewGuid(); var userB = Guid.NewGuid();
		await SeedUserTaskIdAsync(userA.ToString(), "A task", ct);
		await SeedUserTaskIdAsync(userB.ToString(), "B task", ct);
		await using var a = CreateAdapter(services, userA.ToString());
		await using var b = CreateAdapter(services, userB.ToString());
		var ids = new List<Guid>();
		foreach (var (adapter, title) in new[] { (a, "A private paused goal"), (b, "B private paused goal") })
		{
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = true, ["timeZoneId"] = "UTC" }, ct);
			var created = ParseJsonElement(await adapter.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = title, ["horizon"] = "Yearly", ["targetPeriod"] = "2026" }, ct));
			var id = created.GetProperty("id").GetGuid(); ids.Add(id);
			var paused = ParseJsonElement(await adapter.CallToolAsync("goal_update", new Dictionary<string, object?> { ["id"] = id, ["status"] = "Paused" }, ct));
			paused.GetProperty("pausedAt").GetDateTime().Should().Be(TenantTestUtcNow);
			paused.GetProperty("status").GetInt32().Should().Be((int)GoalStatus.Paused);
		}
		var foreign = ParseJsonElement(await a.CallToolAsync("goal_update", new Dictionary<string, object?> { ["id"] = ids[1], ["status"] = "Active" }, ct));
		foreign.GetProperty("error").GetString().Should().Contain("not found");
		var handler = new WeeklyReviewTelegramHandler(services.GetRequiredService<IServiceScopeFactory>(),
			services.GetRequiredService<IPlannerSessionContextAccessor>(), services.GetRequiredService<PerUserAppInitializationService>());
		var account = new TelegramLinkedAccount(userA, 12345, 12345, PlanningMode.General);
		var summary = await handler.HandleAsync(account, "", ct);
		summary.Should().Contain("A private paused goal").And.Contain("paused today").And.NotContain("B private paused goal");
		var aView = ParseJsonElement(await a.CallToolAsync("weekly_review_open", cancellationToken: ct));
		aView.TryGetProperty("pausedGoalsMention", out _).Should().BeFalse();
		aView.GetProperty("report").GetProperty("goals").GetProperty("activeCount").GetInt32().Should().Be(0);
		var bView = ParseJsonElement(await b.CallToolAsync("weekly_review_open", cancellationToken: ct));
		bView.GetProperty("pausedGoalsMention").GetString().Should().Contain("B private paused goal").And.NotContain("A private paused goal");
		var listed = ParseJsonElement(await a.CallToolAsync("goal_list", new Dictionary<string, object?> { ["includeInactive"] = true }, ct));
		listed.EnumerateArray().Should().ContainSingle();
		var resumed = ParseJsonElement(await a.CallToolAsync("goal_update", new Dictionary<string, object?> { ["id"] = ids[0], ["status"] = "Active" }, ct));
		resumed.TryGetProperty("pausedAt", out _).Should().BeFalse();
		resumed.GetProperty("lastResumedAt").GetDateTime().Should().Be(TenantTestUtcNow);
	}

	[Fact]
	public async Task LongPausedGoalReviewIsSentInTelegramMessagesWithoutLosingClaimedMention()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var userId = Guid.NewGuid();
		await SeedUserTaskIdAsync(userId.ToString(), "Task", ct);
		var titles = Enumerable.Range(0, 20).Select(i => $"Goal {i:D2} " + new string('x', 240)).ToArray();
		await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db")}").Options))
		{
			foreach (var title in titles)
			{
				var goal = new Goal(title, GoalHorizon.Yearly);
				goal.SetStatus(GoalStatus.Paused, TenantTestUtcNow.AddDays(-21));
				db.Goals.Add(goal);
			}
			await db.SaveChangesAsync(ct);
		}
		await using var adapter = CreateAdapter(services, userId.ToString());
		await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = false, ["timeZoneId"] = "UTC" }, ct);
		var handler = new WeeklyReviewTelegramHandler(services.GetRequiredService<IServiceScopeFactory>(),
			services.GetRequiredService<IPlannerSessionContextAccessor>(), services.GetRequiredService<PerUserAppInitializationService>());
		var account = new TelegramLinkedAccount(userId, 12345, 12345, PlanningMode.General);
		var summary = await handler.HandleAsync(account, "", ct);
		summary.Length.Should().BeGreaterThan(4096);
		summary.Should().Contain("20 goals are paused").And.Contain("paused 3 weeks ago");
		using var delivery = new RecordingTelegramDelivery();
		using var client = new TelegramBotClient(new HttpClient(delivery) { BaseAddress = new Uri("https://api.telegram.org/") },
			Options.Create(new TelegramOptions { BotToken = "test-token" }));
		await client.SendTextAsync(account.ChatId, summary, ct);
		delivery.Messages.Should().HaveCountGreaterThan(1).And.OnlyContain(text => text.Length <= 4096);
		var delivered = string.Concat(delivery.Messages);
		delivered.Should().Be(summary);
		foreach (var title in titles) delivered.Should().Contain(title);
		(await handler.HandleAsync(account, "", ct)).Should().NotContain("20 goals are paused");
	}

	[Fact]
	public async Task WeeklyReview_StateReportsAndActionsStayWithinUserScope()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var own = await SeedUserTaskIdAsync("user-a", "Own task", ct);
		var other = await SeedUserTaskIdAsync("user-b", "Other private task", ct);
		await using var a = CreateAdapter(services, "user-a");
		await using var b = CreateAdapter(services, "user-b");
		foreach (var (adapter, id) in new[] { (a, own), (b, other) })
		{
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = true, ["timeZoneId"] = "UTC" }, ct);
			await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = TenantTestUtcNow.AddDays(-1).ToString("O") }, ct);
		}
		var view = ParseJsonElement(await a.CallToolAsync("weekly_review_open", cancellationToken: ct));
		view.ToString().Should().NotContain("Other private");
		var reviewId = view.GetProperty("review").GetProperty("id").GetGuid();
		var row = view.GetProperty("report").GetProperty("tasks")[0];
		var revision = row.GetProperty("revision").GetString();
		var transition = () => b.CallToolAsync("weekly_review_transition", new Dictionary<string, object?> { ["reviewId"] = reviewId, ["action"] = "complete" }, ct);
		await transition.Should().ThrowAsync<KeyNotFoundException>();
		var args = new Dictionary<string, object?> { ["reviewId"] = reviewId, ["taskId"] = other, ["revision"] = revision, ["action"] = "complete" };
		ParseJsonElement(await a.CallToolAsync("weekly_review_apply", args, ct)).GetProperty("applied").GetBoolean().Should().BeFalse();
		args["taskId"] = own; args["action"] = "clearDeadline";
		var result = ParseJsonElement(await a.CallToolAsync("weekly_review_apply", args, ct));
		result.GetProperty("applied").GetBoolean().Should().BeTrue();
		result.GetProperty("task").GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		ParseJsonElement(await b.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = other }, ct))
			.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
		await a.CallToolAsync("weekly_review_transition", new Dictionary<string, object?> { ["reviewId"] = reviewId, ["action"] = "complete" }, ct);
		(await a.CallToolAsync("weekly_review_offer", cancellationToken: ct)).Should().NotContain("Ready for");
		(await b.CallToolAsync("weekly_review_offer", cancellationToken: ct)).Should().Contain("Ready for");
	}

	[Fact]
	public async Task ScheduledDeliveryUsesTrustedActiveLinkedAccountAndItsOwnCounts_AndRechecksUnlink()
	{
		var ct = TestContext.Current.CancellationToken;
		var bot = Substitute.For<ITelegramBotClient>();
		await using var services = BuildTenantServiceProvider(collection =>
		{
			collection.AddDbContext<AuthDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(_tempDirectory, "auth.db")}"));
			collection.AddSingleton(bot);
			collection.AddSingleton<IOptions<TelegramOptions>>(Options.Create(new TelegramOptions { Enabled = true }));
			collection.AddSingleton<IWeeklyReminderHost, WeeklyReminderHost>();
			collection.AddSingleton<WeeklyReminderDispatcher>();
		});
		var userA = new User("a@example.test", "alpha", "hash");
		var userB = new User("b@example.test", "bravo", "hash");
		using (var scope = services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>(); await db.Database.MigrateAsync(ct);
			db.AddRange(userA, userB,
				new TelegramAccountLink { Id = Guid.NewGuid(), UserId = userA.Id, TelegramUserId = 101, ChatId = 201 },
				new TelegramAccountLink { Id = Guid.NewGuid(), UserId = userB.Id, TelegramUserId = 102, ChatId = 202 });
			await db.SaveChangesAsync(ct);
		}
		foreach (var (user, count) in new[] { (userA, 1), (userB, 2) })
		{
			await using var adapter = CreateAdapter(services, user.Id.ToString());
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?>
				{ ["enabled"] = true, ["timeZoneId"] = "UTC", ["weekStart"] = 4, ["reminderTime"] = "12:00" }, ct);
			for (var i = 0; i < count; i++)
			{
				var id = await SeedUserTaskIdAsync(user.Id.ToString(), "PRIVATE", ct);
				await adapter.CallToolAsync("taskitem_set_focus_date", new Dictionary<string, object?> { ["id"] = id, ["focusDate"] = TenantTestUtcNow.ToString("O") }, ct);
			}
		}
		var bothDeliveries = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var arrivals = 0;
		var accessor = services.GetRequiredService<IPlannerSessionContextAccessor>();
		bot.SendTextAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			var expectedUser = call.Arg<long>() == 201 ? userA.Id : userB.Id;
			accessor.Current!.UserId.Should().Be(expectedUser.ToString());
			if (Interlocked.Increment(ref arrivals) == 2) bothDeliveries.TrySetResult();
			await bothDeliveries.Task.WaitAsync(TimeSpan.FromSeconds(10), call.Arg<CancellationToken>());
			accessor.Current!.UserId.Should().Be(expectedUser.ToString());
		});
		var dispatcher = services.GetRequiredService<WeeklyReminderDispatcher>();
		await dispatcher.RunOnceAsync(ct); await dispatcher.RunOnceAsync(ct);
		arrivals.Should().Be(2);
		foreach (var user in new[] { userA, userB })
		{
			await using var adapter = CreateAdapter(services, user.Id.ToString());
			var view = ParseJsonElement(await adapter.CallToolAsync("weekly_review_open", cancellationToken: ct));
			view.GetProperty("review").GetProperty("delivery").GetString().Should().Be("delivered");
		}
		await bot.Received(1).SendTextAsync(201, Arg.Is<string>(s => s != null && s.Contains("1 unfinished") && !s.Contains("PRIVATE")), Arg.Any<CancellationToken>());
		await bot.Received(1).SendTextAsync(202, Arg.Is<string>(s => s != null && s.Contains("2 unfinished") && !s.Contains("PRIVATE")), Arg.Any<CancellationToken>());
		var host = services.GetRequiredService<IWeeklyReminderHost>();
		await host.WithUserAsync(userA.Id, async (_, send) =>
		{
			using var scope = services.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
			await db.TelegramAccountLinks.Where(l => l.UserId == userA.Id).ExecuteDeleteAsync(ct);
			var removedSend = () => send("Must not send", ct);
			await removedSend.Should().ThrowAsync<InvalidOperationException>();
		}, ct);
		(await host.GetLinkedUsersAsync(ct)).Should().Equal(userB.Id);
		await host.WithUserAsync(Guid.NewGuid(), (_, _) => throw new Exception("Unknown user must not be scoped"), ct);
	}

	[Fact]
	public async Task General_ScriptedSuitablePlanningTurnOffersOnceAcrossSessions_WithoutTaskChanges()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var id = await SeedUserTaskIdAsync("user-a", "Keep task", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = true, ["timeZoneId"] = "UTC" }, ct);
		await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = TenantTestUtcNow.AddDays(-1).ToString("O") }, ct);
		var before = await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct);
		for (var session = 0; session < 2; session++)
		{
			using var provider = new WeeklyOfferProvider();
			await using var chat = new ChatServiceAdapter("AIza" + new string('x', 35), "test-model", mcpAdapter: adapter, httpClientFactory: provider);
			await using var planner = new PlanningService(chat, null, services.GetRequiredService<ILogger<PlanningService>>());
			await planner.SwitchModeAsync(PlanningMode.General, ct);
			var response = await planner.GetResponseAsync("Help me prioritize my week", ct);
			response.Message.Should().StartWith("Your immediate planning answer.");
			response.Message.Contains("Ready for").Should().Be(session == 0);
			planner.CurrentMode.Should().Be(PlanningMode.General);
		}
		(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct)).Should().Be(before);
	}

	[Fact]
	public async Task TelegramReviewCommandsShareWebStateWithoutChangingMode()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var userId = Guid.NewGuid();
		var handler = new WeeklyReviewTelegramHandler(services.GetRequiredService<IServiceScopeFactory>(),
			services.GetRequiredService<IPlannerSessionContextAccessor>(), services.GetRequiredService<PerUserAppInitializationService>());
		var account = new TelegramLinkedAccount(userId, 10, 20, PlanningMode.DayWork);
		(await handler.HandleAsync(account, "", ct)).Should().Contain("timezone");
		await handler.HandleAsync(account, "enable Europe/London 1 18:00", ct);
		(await handler.HandleAsync(account, "", ct)).Should().NotContain("formulate 1–3 goals");
		await using (var initialChat = CreateAdapter(services, userId.ToString()))
		{
			var first = ParseJsonElement(await initialChat.CallToolAsync("weekly_review_open", cancellationToken: ct));
			first.GetProperty("suggestCreatingGoals").GetBoolean().Should().BeTrue();
			var again = ParseJsonElement(await initialChat.CallToolAsync("weekly_review_open", cancellationToken: ct));
			again.GetProperty("suggestCreatingGoals").GetBoolean().Should().BeFalse();
		}
		await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={Path.Combine(_tempDirectory, $"ownplanner-user-{userId}.db")}").Options))
		{
			db.Goals.Add(new Goal("Telegram goal", GoalHorizon.Monthly, targetPeriod: "2026-07"));
			await db.SaveChangesAsync(ct);
		}
		var summary = await handler.HandleAsync(account, "", ct);
		summary.Should().Contain("Telegram goal").And.Contain("no next step").And.Contain("no plan for week")
			.And.Contain("target period passed").And.NotContain("targetPeriodPassed");
		summary.IndexOf("Active goals:", StringComparison.Ordinal).Should().BeLessThan(summary.IndexOf("Remaining tasks:", StringComparison.Ordinal));
		await handler.HandleAsync(account, "skip", ct);
		await using var web = CreateAdapter(services, userId.ToString());
		var view = ParseJsonElement(await web.CallToolAsync("weekly_review_open", cancellationToken: ct));
		view.GetProperty("review").GetProperty("status").GetString().Should().Be("skipped");
		await handler.HandleAsync(account, "disable", ct);
		var settings = ParseJsonElement(await web.CallToolAsync("weekly_review_settings_get", cancellationToken: ct));
		settings.GetProperty("enabled").GetBoolean().Should().BeFalse();
		settings.GetProperty("timeZoneId").GetString().Should().Be("Europe/London");
		account.Mode.Should().Be(PlanningMode.DayWork);
	}

	[Fact]
	public async Task ScriptedReviewActionsReportPartialFailureAfterConcurrentTaskChange()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var own = await SeedUserTaskIdAsync("user-a", "Keep", ct);
		var changed = await SeedUserTaskIdAsync("user-a", "Changed", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = true, ["timeZoneId"] = "UTC" }, ct);
		foreach (var id in new[] { own, changed })
			await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = TenantTestUtcNow.AddDays(-1).ToString("O") }, ct);
		using var provider = new WeeklyActionProvider(adapter, changed);
		await using var chat = new ChatServiceAdapter("AIza" + new string('x', 35), "test-model", mcpAdapter: adapter, httpClientFactory: provider);
		await using var planner = new PlanningService(chat, null, services.GetRequiredService<ILogger<PlanningService>>());
		await planner.SwitchModeAsync(PlanningMode.General, ct);
		var response = await planner.GetResponseAsync("Open my weekly review and remove deadlines from these two identified tasks", ct);
		response.Message.Should().Be("One deadline removed; one changed task skipped.");
		ParseJsonElement(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = own }, ct))
			.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		ParseJsonElement(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = changed }, ct))
			.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.String);
		var view = ParseJsonElement(await adapter.CallToolAsync("weekly_review_open", cancellationToken: ct));
		view.GetProperty("review").GetProperty("status").GetString().Should().Be("inProgress");
	}

	private sealed class RecordingTelegramDelivery : HttpMessageHandler
	{
		public List<string> Messages { get; } = [];
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
			var text = body.GetProperty("text").GetString()!;
			Messages.Add(text);
			return new HttpResponseMessage(text.Length <= 4096 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.BadRequest);
		}
	}

	private sealed class WeeklyActionProvider(IMcpAdapter adapter, Guid changed) : HttpMessageHandler, IHttpClientFactory
	{
		private int _round;
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var body = ParseJsonElement(await request.Content!.ReadAsStringAsync(ct));
			var responses = body.GetProperty("contents").EnumerateArray().SelectMany(x => x.GetProperty("parts").EnumerateArray())
				.Where(x => x.TryGetProperty("functionResponse", out _)).Select(x => x.GetProperty("functionResponse")).ToArray();
			object[] parts;
			if (_round == 0) parts = [new { functionCall = new { name = "skill_load", args = new { skillId = "weekly_planning" } } }];
			else if (_round == 1) parts = [new { functionCall = new { name = "weekly_review_open", args = new { } } }];
			else if (_round == 2)
			{
				var result = ParseJsonElement(responses.Last(x => x.GetProperty("name").GetString() == "weekly_review_open").GetProperty("response").GetProperty("result").GetString()!);
				await adapter.CallToolAsync("taskitem_complete", new Dictionary<string, object?> { ["id"] = changed }, ct);
				parts = result.GetProperty("report").GetProperty("tasks").EnumerateArray().Select(row => (object)new
				{
					functionCall = new { name = "weekly_review_apply", args = new { reviewId = result.GetProperty("review").GetProperty("id").GetString(),
						taskId = row.GetProperty("id").GetString(), revision = row.GetProperty("revision").GetString(), action = "clearDeadline" } }
				}).ToArray();
			}
			else
			{
				var applied = responses.Where(x => x.GetProperty("name").GetString() == "weekly_review_apply")
					.Select(x => ParseJsonElement(x.GetProperty("response").GetProperty("result").GetString()!).GetProperty("applied").GetBoolean()).ToArray();
				applied.Should().BeEquivalentTo([true, false]);
				parts = [new { text = "One deadline removed; one changed task skipped." }];
			}
			_round++;
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
				new { candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } } }), System.Text.Encoding.UTF8, "application/json") };
		}
	}

	private sealed class WeeklyOfferProvider : HttpMessageHandler, IHttpClientFactory
	{
		private int _round;
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var body = ParseJsonElement(await request.Content!.ReadAsStringAsync(ct));
			object[] parts;
			if (_round++ == 0)
				parts = [new { functionCall = new { name = "weekly_review_offer", args = new { } } }];
			else
			{
				var result = body.GetProperty("contents").EnumerateArray().SelectMany(x => x.GetProperty("parts").EnumerateArray())
					.Where(x => x.TryGetProperty("functionResponse", out _)).Select(x => x.GetProperty("functionResponse"))
					.Last(x => x.GetProperty("name").GetString() == "weekly_review_offer").GetProperty("response").GetProperty("result").GetString()!;
				var json = ParseJsonElement(result);
				parts = [new { text = "Your immediate planning answer." + (json.TryGetProperty("invitation", out var invitation) ? " " + invitation.GetString() : "") }];
			}
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
				new { candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } } }), System.Text.Encoding.UTF8, "application/json") };
		}
	}
}
