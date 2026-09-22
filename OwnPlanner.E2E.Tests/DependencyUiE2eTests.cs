using Microsoft.Playwright;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class DependencyUiE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Theory]
	[InlineData("light", 1440)]
	[InlineData("dark", 1440)]
	[InlineData("light", 390)]
	[InlineData("dark", 390)]
	public async Task PlannerAndSettings_WorkInBothThemesAndLayouts(string theme, int width)
	{
		await Page.SetViewportSizeAsync(width, 900);
		await Page.AddInitScriptAsync($"localStorage.setItem('ownplanner-color-mode', '{theme}')");
		await RegisterAsync(Page, CreateUser());
		await Expect(Page.Locator("html")).ToHaveCSSAsync("color-scheme", theme);
		var suffix = Guid.NewGuid().ToString("N");
		var prompt = Application.Scenarios.Register(async adapter =>
		{
			var mcp = adapter ?? throw new InvalidOperationException("MCP adapter is required.");
			await mcp.CallToolAsync("taskitem_create", new Dictionary<string, object?> { ["title"] = $"Task {suffix}", ["taskListId"] = WellKnownIds.InboxTaskList, ["description"] = "Task details" });
			await mcp.CallToolAsync("goal_create", new Dictionary<string, object?> { ["title"] = $"Goal {suffix}", ["horizon"] = "Yearly", ["description"] = "Goal details" });
			await mcp.CallToolAsync("noteitem_create", new Dictionary<string, object?> { ["title"] = $"Note {suffix}", ["noteListId"] = WellKnownIds.InboxNoteList, ["content"] = "Note details" });
			return new ChatTurnResult("Planner data ready", 100);
		});
		await SendPromptAsync(Page, prompt);
		await Expect(Page.GetByText("Planner data ready", new() { Exact = true })).ToBeVisibleAsync();

		if (width >= 600)
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Collapse navigation", Exact = true }).ClickAsync();
			await Page.GetByRole(AriaRole.Button, new() { Name = "Expand navigation", Exact = true }).ClickAsync();
		}

		foreach (var (section, title, details) in new[] { ("Tasks", $"Task {suffix}", "Task details"), ("Goals", $"Goal {suffix}", "Goal details"), ("Notes", $"Note {suffix}", "Note details") })
		{
			await NavigateAsync(section, width);
			await Page.GetByLabel($"Search {section.ToLowerInvariant()}").FillAsync(title);
			await Page.GetByRole(AriaRole.List, new() { Name = $"{section} results", Exact = true }).GetByText(title, new() { Exact = true }).ClickAsync();
			var inspector = Page.GetByLabel($"{section} details inspector", new() { Exact = true });
			await Expect(inspector).ToBeVisibleAsync();
			await Expect(inspector.GetByText(details, new() { Exact = true })).ToBeVisibleAsync();
			await Expect(inspector).ToBeInViewportAsync(new() { Ratio = 1 });
			var bounds = await inspector.BoundingBoxAsync();
			Assert.NotNull(bounds);
			Assert.True(bounds.X >= -1 && bounds.X + bounds.Width <= width + 1);
			Assert.True(Math.Abs(bounds.Height - 900) < 2);
			await inspector.GetByRole(AriaRole.Button, new() { Name = "Close details" }).ClickAsync();
			await Expect(inspector).ToHaveCountAsync(0);
		}

		if (width >= 600)
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Collapse assistant", Exact = true }).ClickAsync();
			await Page.GetByRole(AriaRole.Button, new() { Name = "Ask OwnPlanner…", Exact = true }).ClickAsync();
			await Expect(Page.GetByText("Planner data ready", new() { Exact = true })).ToBeVisibleAsync();
		}
		else
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true }).ClickAsync();
			await Expect(Page.GetByText("Planner data ready", new() { Exact = true })).ToBeVisibleAsync();
		}

		await NavigateAsync("Settings", width);
		await Page.GetByLabel("Token name", new() { Exact = true }).FillAsync("UI regression");
		await Page.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("Token created. Copy it now; it will not be shown again.", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Link, new() { Name = "Back to chat" }).ClickAsync();
		if (width < 600) await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }).ClickAsync();
		await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
	}

	private async Task NavigateAsync(string section, int width)
	{
		if (width < 600) await Page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true }).ClickAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = section, Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = section, Exact = true })).ToBeVisibleAsync();
	}
}
