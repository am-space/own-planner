using FluentAssertions;
using Microsoft.Playwright;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class WeeklyReviewE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
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
