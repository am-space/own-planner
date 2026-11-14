using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Playwright;
using OwnPlanner.E2E.Tests.Infrastructure;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class WeeklyReviewE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Theory]
	[InlineData(1440)]
	[InlineData(390)]
	public async Task NavigationAndSetup_LeadToDedicatedManualReview_WithoutRemindersOrTelegram(int width)
	{
		await RegisterAsync(Page, CreateUser());
		await Page.SetViewportSizeAsync(width, 844);
		if (width < 600)
			await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Weekly Review", Exact = true }).ClickAsync();
		await Expect(Page).ToHaveURLAsync(new Regex("/planner/review$"));
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Weekly Review", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("Choose your review timezone", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true })).ToHaveCountAsync(0);
		await Page.GetByRole(AriaRole.Link, new() { Name = "Weekly Review settings", Exact = true }).ClickAsync();
		await Expect(Page).ToHaveURLAsync(new Regex("/settings#weekly-review$"));
		await Expect(Page.Locator("main h6").First).ToHaveTextAsync("Weekly review");
		await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Enable weekly reminders" })).Not.ToBeCheckedAsync();
		await Page.GetByRole(AriaRole.Combobox, new() { Name = "Review timezone" }).FillAsync("UTC");
		await Page.GetByRole(AriaRole.Option, new() { Name = "UTC", Exact = true }).ClickAsync();
		var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Page.RouteAsync("**/api/weekly-review/settings", async route =>
		{
			if (route.Request.Method == "PUT") await releaseSave.Task;
			await route.ContinueAsync();
		});
		var openReview = Page.GetByRole(AriaRole.Link, new() { Name = "Open weekly review", Exact = true });
		try
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Save weekly review settings" }).ClickAsync();
			await Expect(openReview).ToBeDisabledAsync();
			await Expect(Page).ToHaveURLAsync(new Regex("/settings#weekly-review$"));
		}
		finally { releaseSave.TrySetResult(); }
		await Expect(Page.GetByText("Weekly review settings saved.", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(openReview).ToBeEnabledAsync();
		await openReview.ClickAsync();
		await Expect(Page.GetByText("No tasks need review.", new() { Exact = true })).ToBeVisibleAsync();
		await Page.ReloadAsync();
		await Expect(Page.GetByText("No tasks need review.", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("UTC · inProgress");
		if (width < 600)
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true }).ClickAsync();
			await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Weekly Review", Exact = true })).ToBeHiddenAsync();
			await Page.GetByRole(AriaRole.Button, new() { Name = "Weekly Review", Exact = true }).ClickAsync();
			await Expect(Page.GetByText("No tasks need review.", new() { Exact = true })).ToBeVisibleAsync();
		}
		await Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("completed");
		await Page.GetByRole(AriaRole.Link, new() { Name = "Review settings", Exact = true }).ClickAsync();
		await Expect(Page.Locator("#weekly-review")).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true })).ToHaveCountAsync(0);
		await Expect(Page.GetByRole(AriaRole.Switch, new() { Name = "Enable weekly reminders" })).Not.ToBeCheckedAsync();
	}

	[Fact]
	public async Task RecurringReviewShowsMixedProgressAndOneRowPerSeries()
	{
		var ct = TestContext.Current.CancellationToken; var user = CreateUser();
		await RegisterAsync(Page, user);
		(await Page.EvaluateAsync<int>("async () => (await fetch('/api/weekly-review/settings', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled: false, timeZoneId: 'UTC', weekStart: 1, reminderTime: '18:00', channel: 'telegram' }) })).status")).Should().Be(200);
		await using var auth = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={Application.AuthDatabasePath}").Options);
		var userId = await auth.Users.Where(u => u.Email == user.Email).Select(u => u.Id).SingleAsync(ct);
		var path = Path.Combine(Application.UserDatabaseDirectory, $"ownplanner-user-{userId}.db");
		var now = DateTime.UtcNow; var today = DateOnly.FromDateTime(now);
		await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options))
		{
			var goal = new Goal("Recurring progress", GoalHorizon.Yearly, targetPeriod: today.Year.ToString());
			var series = new TaskItem("Daily practice series", WellKnownIds.InboxTaskList, goalId: goal.Id);
			series.ConfigureRecurrence(new(RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday),
				today.AddDays(-8), null, MissedOccurrencePolicy.RetainEachObligation));
			db.AddRange(goal, series);
			db.TaskOccurrenceFacts.Add(new() { TaskId = series.Id, OccurrenceDate = today, Status = TaskOccurrenceStatus.Completed, CompletedAt = now });
			await db.SaveChangesAsync(ct);
		}
		await Page.GotoAsync("/planner/review");
		await Expect(Page.GetByText("Recurring progress", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("Completed work items in the last 7 days: 1", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.GetByText("Goal work: 1 of 1 work items completed", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.Locator("main ul li")).ToHaveCountAsync(1);
		await Expect(Page.Locator("main ul li")).ToContainTextAsync("Daily practice series");
		await Expect(Page.Locator("main ul li")).ToContainTextAsync("pending occurrences");
		await Expect(Page.GetByText("No plan for this week:", new() { Exact = false })).ToHaveCountAsync(0);
		await Expect(Page.GetByText("Completed sources: Daily practice series", new() { Exact = true })).ToBeVisibleAsync();
		await Page.ReloadAsync(); await Expect(Page.Locator("main ul li")).ToHaveCountAsync(1);
	}

	[Fact]
	public async Task ReviewPagesAndTransitions_PersistSharedState_WithoutChangingTasks()
	{
		await RegisterAsync(Page, CreateUser());
		var prompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter!;
			await mcp.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = false, ["timeZoneId"] = "UTC" });
			for (var index = 0; index < 21; index++)
				await mcp.CallToolAsync("taskitem_create", new Dictionary<string, object?>
				{
					["title"] = $"Review task {index}", ["taskListId"] = WellKnownIds.InboxTaskList, ["dueAt"] = "2000-01-01T00:00:00Z"
				});
			return new ChatTurnResult("Review tasks prepared", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Review tasks prepared", new() { Exact = true })).ToBeVisibleAsync();
		var originalTasks = await ReadTasksAsync();
		await Page.GotoAsync("/planner/review");
		await Expect(Page.GetByText("1–20 of 21 tasks", new() { Exact = true })).ToBeVisibleAsync();
		var firstPage = await Page.Locator("main ul li").AllTextContentsAsync();
		firstPage.Should().HaveCount(20);
		await Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("21–21 of 21 tasks", new() { Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true })).ToBeDisabledAsync();
		var lastPage = await Page.Locator("main ul li").AllTextContentsAsync();
		lastPage.Should().ContainSingle();
		firstPage.Should().NotContain(lastPage.Single());
		await Page.GetByRole(AriaRole.Button, new() { Name = "Previous", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("1–20 of 21 tasks", new() { Exact = true })).ToBeVisibleAsync();
		(await Page.Locator("main ul li").AllTextContentsAsync()).Should().Equal(firstPage);

		await Page.GetByLabel("Remind me later (UTC)", new() { Exact = true }).FillAsync("2000-01-01T12:00");
		await Page.GetByRole(AriaRole.Button, new() { Name = "Defer review", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("future time");
		await Expect(Page.GetByText("1–20 of 21 tasks", new() { Exact = true })).ToBeVisibleAsync();
		var futureTime = DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
		await Page.GetByLabel("Remind me later (UTC)", new() { Exact = true }).FillAsync(futureTime);
		await Page.GetByRole(AriaRole.Button, new() { Name = "Defer review", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("deferred");
		await Page.ReloadAsync();
		await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("deferred");
		await Page.GetByRole(AriaRole.Button, new() { Name = "Skip this week", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("skipped");
		await Page.ReloadAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true })).ToBeDisabledAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Skip this week", Exact = true })).ToBeDisabledAsync();
		(await ReadTasksAsync()).Should().Be(originalTasks);

		async Task<string> ReadTasksAsync() => await Page.EvaluateAsync<string>("async () => JSON.stringify((await (await fetch('/api/planner/tasks?limit=100')).json()).items)");
	}

	[Theory]
	[InlineData("settings")]
	[InlineData("open?*")]
	public async Task LoadingAndRequestErrors_AllowRetryWithoutLosingThePage(string endpoint)
	{
		await RegisterAsync(Page, CreateUser());
		(await Page.EvaluateAsync<int>("async () => (await fetch('/api/weekly-review/settings', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled: false, timeZoneId: 'UTC', weekStart: 1, reminderTime: '18:00', channel: 'telegram' }) })).status")).Should().Be(200);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pattern = $"**/api/weekly-review/{endpoint}";
		await Page.RouteAsync(pattern, async route =>
		{
			await release.Task;
			await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Review temporarily unavailable\"}" });
		});
		try
		{
			await Page.GotoAsync("/planner/review");
			await Expect(Page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Loading weekly review…");
		}
		finally { release.TrySetResult(); }
		await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Review temporarily unavailable");
		await Expect(Page.GetByText("Loading weekly review…", new() { Exact = true })).ToHaveCountAsync(0);
		await Page.UnrouteAsync(pattern);
		await Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("No tasks need review.", new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task ReviewDirectRoute_RequiresAuthentication()
	{
		await Page.GotoAsync("/planner/review");
		await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Weekly Review", Exact = true })).ToHaveCountAsync(0);
	}

	[Fact]
	public async Task PausedGoalsAreQueryableAndReviewGuidanceIsDisplayedOnlyOnce()
	{
		await RegisterAsync(Page, CreateUser());
		var prompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter!;
			for (var index = 0; index < 7; index++)
			{
				var created = await mcp.CallToolAsync("goal_create", new Dictionary<string, object?>
				{
					["title"] = index == 0 ? "Paused Spanish" : $"Active goal {index}", ["horizon"] = "Yearly", ["targetPeriod"] = "2026"
				});
				if (index == 0)
				{
					using var json = System.Text.Json.JsonDocument.Parse(created);
					await mcp.CallToolAsync("goal_update", new Dictionary<string, object?> { ["id"] = json.RootElement.GetProperty("id").GetGuid(), ["status"] = "Paused" });
				}
			}
			await mcp.CallToolAsync("weekly_review_configure", new Dictionary<string, object?> { ["enabled"] = false, ["timeZoneId"] = "UTC" });
			return new ChatTurnResult("Goals prepared", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Goals prepared", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GotoAsync("/planner/goals?status=Paused");
		await Expect(Page.GetByText("Paused Spanish", new() { Exact = true }).First).ToBeVisibleAsync();
		await Expect(Page.GetByText("Active goal 1", new() { Exact = true })).ToHaveCountAsync(0);
		await Page.GotoAsync("/settings");
		var open = Page.GetByRole(AriaRole.Link, new() { Name = "Open weekly review", Exact = true });
		await open.ClickAsync();
		await Expect(Page.GetByText("You now have 6 active goals.", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.GetByText("1 goal is paused:", new() { Exact = false })).ToContainTextAsync("Paused Spanish");
		await Page.ReloadAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true })).ToBeEnabledAsync();
		await Expect(Page.GetByText("You now have 6 active goals.", new() { Exact = false })).ToHaveCountAsync(0);
		await Expect(Page.GetByText("1 goal is paused:", new() { Exact = false })).ToHaveCountAsync(0);
	}

	[Fact]
	public async Task PreferencesRequireExplicitTimezone_PersistAndShareReviewState_WithTenantIsolation()
	{
		await RegisterAsync(Page, CreateUser());
		await Page.GotoAsync("/settings");
		var enabled = Page.GetByRole(AriaRole.Switch, new() { Name = "Enable weekly reminders" });
		await Expect(enabled).Not.ToBeCheckedAsync();
		await enabled.CheckAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save weekly review settings" })).ToBeDisabledAsync();
		await Page.GetByRole(AriaRole.Combobox, new() { Name = "Review timezone" }).FillAsync("UTC");
		await Page.GetByRole(AriaRole.Option, new() { Name = "UTC", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Save weekly review settings" }).ClickAsync();
		await Expect(Page.GetByText("Weekly review settings saved.", new() { Exact = true })).ToBeVisibleAsync();
		await Page.ReloadAsync();
		await Expect(enabled).ToBeCheckedAsync();
		await Page.GetByRole(AriaRole.Link, new() { Name = "Open weekly review", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("No tasks need review.")).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Finish review", Exact = true })).ToBeDisabledAsync();
		var id = await Page.EvaluateAsync<string>("async () => (await (await fetch('/api/weekly-review/open', { method: 'POST' })).json()).review.id");
		var state = await Page.EvaluateAsync<string>("async () => (await (await fetch('/api/weekly-review/open', { method: 'POST' })).json()).review.status");
		state.Should().Be("completed");

		await using var second = await Browser.NewContextAsync(CreateContextOptions());
		var other = await second.NewPageAsync();
		await RegisterAsync(other, CreateUser());
		var otherEnabled = await other.EvaluateAsync<bool>("async () => (await (await fetch('/api/weekly-review/settings')).json()).enabled");
		otherEnabled.Should().BeFalse();
		var status = await other.EvaluateAsync<int>("async id => (await fetch(`/api/weekly-review/${id}/transition`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ action: 'skip' }) })).status", id);
		status.Should().Be(404);
		await using var anonymous = await Browser.NewContextAsync(CreateContextOptions());
		var anonymousPage = await anonymous.NewPageAsync();
		await anonymousPage.GotoAsync("/login");
		(await anonymousPage.EvaluateAsync<int>("async () => (await fetch('/api/weekly-review/settings')).status")).Should().Be(401);
	}
}
