using Microsoft.Playwright;

namespace OwnPlanner.Deployment.Tests;

[Trait("Category", "LiveAi")]
public sealed class LiveAiDeploymentTests : DeploymentTestBase
{
	[Fact]
	public async Task GeminiCreatesRequestedTask_AndPersistedStateIsVisible()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("OWNPLANNER_RUN_LIVE_AI"), "true", StringComparison.OrdinalIgnoreCase))
		{
			Assert.Skip("Set OWNPLANNER_RUN_LIVE_AI=true to authorize the bounded live Gemini test.");
		}

		await RunWithArtifactsAsync(nameof(GeminiCreatesRequestedTask_AndPersistedStateIsVisible), async session =>
		{
			await RegisterAsync(session.Page, CreateUser("deployment-live-ai"));
			var taskTitle = $"Live AI deployment task {Guid.NewGuid():N}";
			var composer = session.Page.GetByRole(AriaRole.Textbox);
			await composer.FillAsync($"Create exactly one task titled '{taskTitle}' in my Inbox. Use the task creation tool now.");
			await composer.PressAsync("Enter");
			await Assertions.Expect(composer).ToHaveAttributeAsync("placeholder", "Waiting for response...", new() { Timeout = 10_000 });
			await Assertions.Expect(composer).ToHaveAttributeAsync(
				"placeholder",
				"Type your message... (Enter to send, Shift+Enter for new line)",
				new() { Timeout = 120_000 });

			await session.Page.GotoAsync($"/planner/tasks?search={Uri.EscapeDataString(taskTitle)}");
			await Assertions.Expect(session.Page.GetByText(taskTitle, new() { Exact = true }))
				.ToBeVisibleAsync(new() { Timeout = 30_000 });
		});
	}
}
