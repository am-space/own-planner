using Microsoft.Playwright;

namespace OwnPlanner.Deployment.Tests;

public abstract class DeploymentTestBase
{
	protected static async Task RegisterAsync(IPage page, DeploymentUser user)
	{
		await page.GotoAsync("/register");
		await page.GetByLabel("Email Address").FillAsync(user.Email);
		await page.GetByLabel("Username").FillAsync(user.Username);
		await page.Locator("#password").FillAsync(user.Password);
		await page.Locator("#confirmPassword").FillAsync(user.Password);
		await page.GetByRole(AriaRole.Checkbox).CheckAsync();
		await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
		await Assertions.Expect(page).ToHaveURLAsync(new Regex("/chat$"), new() { Timeout = 30_000 });
		await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!" }))
			.ToBeVisibleAsync();
	}

	protected static DeploymentUser CreateUser(string prefix)
	{
		var suffix = Guid.NewGuid().ToString("N");
		return new DeploymentUser(
			$"{prefix}-{suffix}@example.test",
			$"{prefix}-{suffix[..12]}",
			"Deploy!Password123");
	}

	protected static async Task RunWithArtifactsAsync(string testName, Func<DeploymentTestSession, Task> action)
	{
		await using var session = await DeploymentTestSession.CreateAsync();
		try
		{
			await action(session);
		}
		catch
		{
			await session.CaptureFailureArtifactsAsync(testName);
			throw;
		}
	}

	protected sealed record DeploymentUser(string Email, string Username, string Password);
}

