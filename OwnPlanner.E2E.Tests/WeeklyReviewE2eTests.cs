using FluentAssertions;
using Microsoft.Playwright;
using OwnPlanner.E2E.Tests.Infrastructure;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class WeeklyReviewE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
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
		var open = Page.GetByRole(AriaRole.Button, new() { Name = "Open weekly review", Exact = true });
		await open.ClickAsync();
		await Expect(Page.GetByText("You now have 6 active goals.", new() { Exact = false })).ToBeVisibleAsync();
		await Expect(Page.GetByText("1 goal is paused:", new() { Exact = false })).ToContainTextAsync("Paused Spanish");
		await open.ClickAsync();
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
		await Page.GetByRole(AriaRole.Button, new() { Name = "Open weekly review", Exact = true }).ClickAsync();
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
