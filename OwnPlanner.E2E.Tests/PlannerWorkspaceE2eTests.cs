using Microsoft.Playwright;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class PlannerWorkspaceE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Fact]
	public async Task TaskWorkspace_PreservesChatAndUrlState_WhileInspectingReadOnlyDetails()
	{
		await Page.SetViewportSizeAsync(1440, 900);
		await RegisterAsync(Page, CreateUser());
		var taskTitle = $"Planner workspace task {Guid.NewGuid():N}";
		var prompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter ?? throw new InvalidOperationException("MCP adapter is required.");
			await mcp.CallToolAsync(
				"taskitem_create",
				new Dictionary<string, object?>
				{
					["title"] = taskTitle,
					["description"] = "A complete private description for the workspace inspector.",
					["taskListId"] = WellKnownIds.InboxTaskList,
				});
			return new ChatTurnResult($"Created {taskTitle}", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		await Page.GetByRole(AriaRole.Button, new() { Name = "Collapse navigation", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Expand navigation", Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Tasks", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Expand navigation", Exact = true }).ClickAsync();
		await Expect(Page).ToHaveURLAsync(new Regex("/planner/tasks"));
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tasks", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		await Page.GetByLabel("Search tasks").FillAsync(taskTitle);
		await Expect(Page).ToHaveURLAsync(new Regex("search="));
		await Expect(Page.GetByText(taskTitle, new() { Exact = true }).First).ToBeVisibleAsync();
		await Expect(Page.GetByText("Inbox", new() { Exact = true })).ToHaveCountAsync(1);
		await Expect(Page.GetByText("A complete private description for the workspace inspector.", new() { Exact = true })).ToHaveCountAsync(0);
		await Page.GetByText(taskTitle, new() { Exact = true }).First.ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = taskTitle, Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByText("A complete private description for the workspace inspector.", new() { Exact = true }).Last).ToBeVisibleAsync();
		var inspectorBounds = await Page.GetByLabel("Tasks details inspector", new() { Exact = true }).BoundingBoxAsync();
		Assert.NotNull(inspectorBounds);
		Assert.True(Math.Abs(inspectorBounds.Y) < 1);
		Assert.True(Math.Abs(inspectorBounds.Height - 900) < 2);

		var collapseAssistant = Page.GetByRole(AriaRole.Button, new() { Name = "Collapse assistant", Exact = true });
		await Expect(collapseAssistant).ToHaveAttributeAsync("aria-expanded", "true");
		await collapseAssistant.ClickAsync();
		await Expect(Page.GetByLabel("Search tasks")).ToHaveValueAsync(taskTitle);
		var expandAssistant = Page.GetByRole(AriaRole.Button, new() { Name = "Ask OwnPlanner…", Exact = true });
		await Expect(expandAssistant).ToHaveAttributeAsync("aria-expanded", "false");
		await expandAssistant.ClickAsync();
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		var taskDeepLink = Page.Url;
		await Page.GetByRole(AriaRole.Button, new() { Name = "Goals", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Goals", Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Notes", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Notes", Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true }).ClickAsync();
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		await Page.GotoAsync(taskDeepLink);
		await Page.ReloadAsync();
		await Expect(Page.GetByLabel("Search tasks")).ToHaveValueAsync(taskTitle);
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = taskTitle, Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task LowercasePlannerDeepLinks_RestoreCanonicalFilterControls()
	{
		await RegisterAsync(Page, CreateUser());

		await Page.GotoAsync("/planner/tasks?status=all");
		await Expect(Page.GetByLabel("Status")).ToHaveTextAsync("All");

		await Page.GotoAsync("/planner/goals?status=active&horizon=quarterly");
		await Expect(Page.GetByLabel("Status")).ToHaveTextAsync("Active");
		await Expect(Page.GetByLabel("Horizon")).ToHaveTextAsync("Quarterly");
	}

	[Fact]
	public async Task TaskWorkspace_PagesThroughTheCompleteMatchingCollection()
	{
		await RegisterAsync(Page, CreateUser());
		var prefix = $"Paged planner {Guid.NewGuid():N}";
		var prompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter ?? throw new InvalidOperationException("MCP adapter is required.");
			for (var index = 0; index < 26; index++)
			{
				await mcp.CallToolAsync(
					"taskitem_create",
					new Dictionary<string, object?>
					{
						["title"] = $"{prefix} {index:D2}",
						["taskListId"] = WellKnownIds.InboxTaskList,
					});
			}
			return new ChatTurnResult("Created paged planner tasks", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Created paged planner tasks", new() { Exact = true })).ToBeVisibleAsync();

		await Page.GetByRole(AriaRole.Button, new() { Name = "Tasks", Exact = true }).ClickAsync();
		await Page.GetByLabel("Search tasks").FillAsync(prefix);
		await Expect(Page.GetByText("1–25 of 26", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
		await Expect(Page).ToHaveURLAsync(new Regex("offset=25"));
		await Expect(Page.GetByText("26–26 of 26", new() { Exact = true })).ToBeVisibleAsync();

		await Page.ReloadAsync();
		await Expect(Page.GetByText("26–26 of 26", new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task MobilePlanner_UsesMutuallyExclusivePlannerAndChatSurfaces()
	{
		await RegisterAsync(Page, CreateUser());
		await Page.SetViewportSizeAsync(390, 844);

		await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Tasks", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tasks", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true })).ToBeVisibleAsync();

		await Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!", Exact = true })).ToBeVisibleAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tasks", Exact = true })).ToBeHiddenAsync();
	}
}
