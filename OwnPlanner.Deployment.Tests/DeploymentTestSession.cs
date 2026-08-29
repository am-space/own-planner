using Microsoft.Playwright;

namespace OwnPlanner.Deployment.Tests;

public sealed class DeploymentTestSession : IAsyncDisposable
{
	private readonly IPlaywright _playwright;
	private readonly IBrowser _browser;
	private readonly IBrowserContext _context;
	private bool _traceStarted;

	private DeploymentTestSession(
		Uri baseAddress,
		IPlaywright playwright,
		IBrowser browser,
		IBrowserContext context,
		IPage page)
	{
		BaseAddress = baseAddress;
		_playwright = playwright;
		_browser = browser;
		_context = context;
		Page = page;
	}

	public Uri BaseAddress { get; }
	public IPage Page { get; }

	public static async Task<DeploymentTestSession> CreateAsync()
	{
		var configuredBaseUrl = Environment.GetEnvironmentVariable("OWNPLANNER_BASE_URL");
		if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseAddress) ||
			(baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
		{
			Assert.Skip("Set OWNPLANNER_BASE_URL to run external deployment tests.");
			throw new InvalidOperationException("Assert.Skip should have interrupted the test.");
		}

		var playwright = await Playwright.CreateAsync();
		try
		{
			var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
			try
			{
				var context = await browser.NewContextAsync(new()
				{
					BaseURL = baseAddress.ToString(),
					IgnoreHTTPSErrors = ReadBooleanEnvironment("OWNPLANNER_IGNORE_HTTPS_ERRORS"),
				});
				await context.Tracing.StartAsync(new()
				{
					Screenshots = true,
					Snapshots = true,
					Sources = true,
				});
				var page = await context.NewPageAsync();
				return new DeploymentTestSession(baseAddress, playwright, browser, context, page)
				{
					_traceStarted = true,
				};
			}
			catch
			{
				await browser.CloseAsync();
				throw;
			}
		}
		catch
		{
			playwright.Dispose();
			throw;
		}
	}

	public async Task CaptureFailureArtifactsAsync(string testName)
	{
		if (!_traceStarted)
		{
			return;
		}

		var artifactDirectory = ResolveArtifactDirectory();
		Directory.CreateDirectory(artifactDirectory);
		var safeTestName = string.Concat(testName.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
		var suffix = Guid.NewGuid().ToString("N");
		var screenshotPath = Path.Combine(artifactDirectory, $"{safeTestName}-{suffix}.png");
		var tracePath = Path.Combine(artifactDirectory, $"{safeTestName}-{suffix}.zip");

		try
		{
			await Page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
		}
		catch (Exception exception)
		{
			TestContext.Current.AddWarning($"Could not capture deployment screenshot: {exception.Message}");
		}

		await _context.Tracing.StopAsync(new() { Path = tracePath });
		_traceStarted = false;
		if (File.Exists(screenshotPath))
		{
			TestContext.Current.AddAttachment("deployment-screenshot", await File.ReadAllBytesAsync(screenshotPath));
		}
		TestContext.Current.AddAttachment("deployment-trace", await File.ReadAllBytesAsync(tracePath));
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			if (_traceStarted)
			{
				await _context.Tracing.StopAsync();
			}
		}
		finally
		{
			await _context.CloseAsync();
			await _browser.CloseAsync();
			_playwright.Dispose();
		}
	}

	private static bool ReadBooleanEnvironment(string name) =>
		bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;

	private static string ResolveArtifactDirectory()
	{
		var configured = Environment.GetEnvironmentVariable("OWNPLANNER_DEPLOYMENT_ARTIFACTS");
		return string.IsNullOrWhiteSpace(configured)
			? Path.Combine(FindRepositoryRoot(), "TestResults", "Deployment")
			: Path.GetFullPath(configured);
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "OwnPlanner.sln")))
			{
				return directory.FullName;
			}
		}

		throw new DirectoryNotFoundException("Could not locate the OwnPlanner repository root.");
	}
}
