using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

public abstract class E2ePageTest(E2eWebApplicationFactory application) : PageTest
{
	private bool _tracingStarted;

	protected E2eWebApplicationFactory Application { get; } = application;

	public override BrowserNewContextOptions ContextOptions() => CreateContextOptions();

	public override async ValueTask InitializeAsync()
	{
		await base.InitializeAsync();
		await Context.Tracing.StartAsync(new()
		{
			Screenshots = true,
			Snapshots = true,
			Sources = true,
		});
		_tracingStarted = true;
	}

	public override async ValueTask DisposeAsync()
	{
		try
		{
			if (_tracingStarted && TestContext.Current.TestState?.Result == TestResult.Failed)
			{
				await CaptureFailureArtifactsAsync();
			}
			else if (_tracingStarted)
			{
				await Context.Tracing.StopAsync();
			}
		}
		finally
		{
			await base.DisposeAsync();
		}
	}

	private async Task CaptureFailureArtifactsAsync()
	{
		try
		{
			Directory.CreateDirectory(Application.ArtifactDirectory);
			var artifactName = $"{GetType().Name}-{Guid.NewGuid():N}";
			var screenshotPath = Path.Combine(Application.ArtifactDirectory, $"{artifactName}.png");
			var tracePath = Path.Combine(Application.ArtifactDirectory, $"{artifactName}.zip");

			try
			{
				await Page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
			}
			catch (Exception screenshotException)
			{
				TestContext.Current.AddWarning($"Could not capture the E2E failure screenshot: {screenshotException.Message}");
			}

			await Context.Tracing.StopAsync(new() { Path = tracePath });
			if (File.Exists(screenshotPath))
			{
				TestContext.Current.AddAttachment("browser-screenshot", await File.ReadAllBytesAsync(screenshotPath));
			}
			TestContext.Current.AddAttachment("playwright-trace", await File.ReadAllBytesAsync(tracePath));

			var serverLog = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "logs"))
				? Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "logs"), "web-*.log")
					.OrderByDescending(File.GetLastWriteTimeUtc)
					.FirstOrDefault()
				: null;
			if (serverLog is not null)
			{
				var serverLogPath = Path.Combine(Application.ArtifactDirectory, $"{artifactName}-server.log");
				await using var source = File.Open(serverLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				await using var destination = File.Create(serverLogPath);
				await source.CopyToAsync(destination);
			}
		}
		catch (Exception exception)
		{
			TestContext.Current.AddWarning($"Could not retain E2E failure artifacts: {exception.Message}");
			try
			{
				await Context.Tracing.StopAsync();
			}
			catch (Exception traceException)
			{
				TestContext.Current.AddWarning($"Could not stop Playwright tracing: {traceException.Message}");
			}
		}
	}

	protected BrowserNewContextOptions CreateContextOptions() => new()
	{
		BaseURL = Application.BaseAddress.ToString(),
	};

	protected static E2eUser CreateUser()
	{
		var suffix = Guid.NewGuid().ToString("N");
		return new E2eUser(
			$"e2e-{suffix}@example.test",
			$"e2e-{suffix[..12]}",
			"E2e!Password123");
	}

	protected async Task RegisterAsync(IPage page, E2eUser user)
	{
		await page.GotoAsync("/register");
		await page.GetByLabel("Email Address").FillAsync(user.Email);
		await page.GetByLabel("Username").FillAsync(user.Username);
		await page.Locator("#password").FillAsync(user.Password);
		await page.Locator("#confirmPassword").FillAsync(user.Password);
		await page.GetByRole(AriaRole.Checkbox).CheckAsync();
		await page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true }).ClickAsync();
		await Expect(page).ToHaveURLAsync(new Regex("/chat$"));
		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!" })).ToBeVisibleAsync();
	}

	protected static async Task SendPromptAsync(IPage page, string prompt)
	{
		var input = page.GetByPlaceholder("Type your message... (Enter to send, Shift+Enter for new line)");
		await input.FillAsync(prompt);
		await input.PressAsync("Enter");
	}

	protected sealed record E2eUser(string Email, string Username, string Password);
}
