using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OwnPlanner.Web.Server;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.E2E.Tests.Infrastructure;

public sealed class E2eWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "ownplanner-e2e", Guid.NewGuid().ToString("N"));
	private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);
	private HttpClient? _startupClient;

	public E2eWebApplicationFactory()
	{
		Directory.CreateDirectory(_temporaryRoot);
		AuthDatabasePath = ResolveTemporaryPath("ownplanner-auth.db");
		UserDatabaseDirectory = ResolveTemporaryPath("users");
		SetEnvironmentVariable("Database__AuthDbPath", AuthDatabasePath);
		SetEnvironmentVariable("Database__UserDbDirectory", UserDatabaseDirectory);
		SetEnvironmentVariable("Email__Provider", "Logging");
		SetEnvironmentVariable("Chat__Gemini__ApiKey", string.Empty);
		UseKestrel(0);
	}

	public Uri BaseAddress { get; private set; } = null!;
	public string ArtifactDirectory { get; } = Path.Combine(FindRepositoryRoot(), "TestResults", "E2E");
	public string AuthDatabasePath { get; }
	internal string TemporaryRoot => _temporaryRoot;
	public string UserDatabaseDirectory { get; }

	public ScriptedChatScenarioRegistry Scenarios => Services.GetRequiredService<ScriptedChatScenarioRegistry>();

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var repositoryRoot = FindRepositoryRoot();
		var frontendDistributionPath = Path.Combine(
			repositoryRoot,
			"OwnPlanner.Web",
			"ownplanner.web.client",
			"dist");
		builder.UseEnvironment("E2E");
		builder.UseContentRoot(Path.Combine(repositoryRoot, "OwnPlanner.Web", "OwnPlanner.Web.Server"));
		builder.UseWebRoot(frontendDistributionPath);
		builder.ConfigureAppConfiguration((_, configuration) =>
		{
			configuration.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Database:AuthDbPath"] = AuthDatabasePath,
				["Database:UserDbDirectory"] = UserDatabaseDirectory,
				["Email:Provider"] = "Logging",
				["Chat:Gemini:ApiKey"] = string.Empty,
			});
		});
		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IChatAdapterFactory>();
			services.AddSingleton<ScriptedChatScenarioRegistry>();
			services.AddSingleton<ScriptedChatAdapterFactory>();
			services.AddSingleton<IChatAdapterFactory>(provider => provider.GetRequiredService<ScriptedChatAdapterFactory>());
			services.AddSingleton<IStartupFilter>(new E2eStaticFilesStartupFilter(frontendDistributionPath));
			services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
			{
				options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
			});
		});
	}

	public ValueTask InitializeAsync()
	{
		ClientOptions.AllowAutoRedirect = false;
		_startupClient = CreateClient();
		BaseAddress = _startupClient.BaseAddress
			?? throw new InvalidOperationException("The E2E Kestrel host did not provide a base address.");

		if (Services.GetRequiredService<IChatAdapterFactory>() is not ScriptedChatAdapterFactory)
		{
			throw new InvalidOperationException("The E2E host resolved the production Gemini adapter factory.");
		}

		return ValueTask.CompletedTask;
	}

	public override async ValueTask DisposeAsync()
	{
		_startupClient?.Dispose();
		await base.DisposeAsync().ConfigureAwait(false);

		if (Directory.Exists(_temporaryRoot))
		{
			Directory.Delete(_temporaryRoot, recursive: true);
		}

		foreach (var (name, value) in _previousEnvironment)
		{
			Environment.SetEnvironmentVariable(name, value);
		}
	}

	private string ResolveTemporaryPath(string relativePath)
	{
		var fullPath = Path.GetFullPath(Path.Combine(_temporaryRoot, relativePath));
		var rootWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_temporaryRoot)) + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("An E2E data path escaped the temporary test root.");
		}

		return fullPath;
	}

	private void SetEnvironmentVariable(string name, string? value)
	{
		_previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
		Environment.SetEnvironmentVariable(name, value);
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
