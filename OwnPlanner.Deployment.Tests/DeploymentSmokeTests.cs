using System.Net;
using System.Text.Json;
using Microsoft.Playwright;

namespace OwnPlanner.Deployment.Tests;

[Trait("Category", "DeploymentSmoke")]
public sealed class DeploymentSmokeTests : DeploymentTestBase
{
	[Fact]
	public async Task HealthRegistrationNavigationAndLogout_WorkAgainstContainer()
	{
		await RunWithArtifactsAsync(nameof(HealthRegistrationNavigationAndLogout_WorkAgainstContainer), async session =>
		{
			using var httpClient = new HttpClient { BaseAddress = session.BaseAddress };
			using var healthResponse = await httpClient.GetAsync("/api/chat/health");
			healthResponse.EnsureSuccessStatusCode();
			using var healthDocument = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
			Assert.Equal("healthy", healthDocument.RootElement.GetProperty("status").GetString());

			var user = CreateUser("deployment-smoke");
			await RegisterAsync(session.Page, user);
			await Assertions.Expect(session.Page.GetByText(user.Username, new() { Exact = true })).ToBeVisibleAsync();

			await session.Page.GotoAsync("/planner/tasks");
			await Assertions.Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Tasks", Exact = true }))
				.ToBeVisibleAsync();
			await Assertions.Expect(session.Page.GetByText("No tasks yet. Ask OwnPlanner to create one from the chat below."))
				.ToBeVisibleAsync();

			await session.Page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }).ClickAsync();
			await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("/login$"));
			var protectedResponse = await session.Page.GotoAsync("/planner/tasks");
			Assert.NotNull(protectedResponse);
			Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)protectedResponse.Status);
			await Assertions.Expect(session.Page).ToHaveURLAsync(new Regex("/login$"));
		});
	}
}
