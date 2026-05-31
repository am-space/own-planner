using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class PerUserAppInitializationServiceTests : IDisposable
{
	private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "ownplanner-init-tests", Guid.NewGuid().ToString("N"));

	[Fact]
	public async Task EnsureInitializedAsync_WhenWaiterIsCancelledAndInitializationLaterFaults_RetriesOnNextCall()
	{
		var firstAttemptStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstAttemptToFail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var counter = new AttemptCounter();

		await using var serviceProvider = BuildServiceProvider((_, _) =>
		{
			var currentAttempt = counter.Increment();
			if (currentAttempt == 1)
			{
				firstAttemptStarted.TrySetResult();
				return FailAfterSignalAsync(allowFirstAttemptToFail.Task);
			}

			return Task.CompletedTask;
		});

		var service = serviceProvider.GetRequiredService<PerUserAppInitializationService>();
		var sessionContext = CreateSessionContext();
		using var cancellationTokenSource = new CancellationTokenSource();

		var firstCall = service.EnsureInitializedAsync(sessionContext, cancellationTokenSource.Token);
		await firstAttemptStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
		var firstInitializationTask = GetCurrentInitializationTask(service, sessionContext.UserId);
		cancellationTokenSource.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstCall);

		allowFirstAttemptToFail.TrySetResult();
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await firstInitializationTask);

		await service.EnsureInitializedAsync(sessionContext, TestContext.Current.CancellationToken);

		counter.Value.Should().Be(2);
	}

	[Fact]
	public async Task EnsureInitializedAsync_WhenInitializationTaskIsCanceled_RetriesOnNextCall()
	{
		var counter = new AttemptCounter();

		await using var serviceProvider = BuildServiceProvider((_, _) =>
		{
			var currentAttempt = counter.Increment();
			return currentAttempt == 1
				? Task.FromCanceled(new CancellationToken(canceled: true))
				: Task.CompletedTask;
		});

		var service = serviceProvider.GetRequiredService<PerUserAppInitializationService>();
		var sessionContext = CreateSessionContext();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.EnsureInitializedAsync(sessionContext, TestContext.Current.CancellationToken));
		await service.EnsureInitializedAsync(sessionContext, TestContext.Current.CancellationToken);

		counter.Value.Should().Be(2);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}
	}

	private ServiceProvider BuildServiceProvider(Func<AppDbContext, CancellationToken, Task> seedAsync)
	{
		Directory.CreateDirectory(_tempDirectory);
		var dbPath = Path.Combine(_tempDirectory, $"planner-{Guid.NewGuid():N}.db");
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddHttpContextAccessor();
		services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
		services.AddScoped<IPlannerDbContextFactory>(_ => new FixedPathTestPlannerDbContextFactory(dbPath));
		services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
		services.AddScoped<IInboxSeeder, DelegatingInboxSeeder>(serviceProvider =>
			new DelegatingInboxSeeder(
				seedAsync,
				serviceProvider.GetRequiredService<AppDbContext>()));
		services.AddSingleton<PerUserAppInitializationService>();
		return services.BuildServiceProvider();
	}

	private static SessionContext CreateSessionContext()
	{
		return new SessionContext
		{
			SessionId = "session-123",
			UserId = "user-456"
		};
	}

	private static Task GetCurrentInitializationTask(PerUserAppInitializationService service, string userId)
	{
		var field = typeof(PerUserAppInitializationService)
			.GetField("_initializations", BindingFlags.Instance | BindingFlags.NonPublic);

		field.Should().NotBeNull();
		var initializations = field.GetValue(service)
			.Should().BeOfType<ConcurrentDictionary<string, Lazy<Task>>>()
			.Subject;

		initializations.TryGetValue(userId, out var initialization).Should().BeTrue();
		initialization.Should().NotBeNull();
		return initialization.Value;
	}

	private static async Task FailAfterSignalAsync(Task signalTask)
	{
		await signalTask.ConfigureAwait(false);
		throw new InvalidOperationException("Initialization failed after waiter cancellation.");
	}


	private sealed class DelegatingInboxSeeder(
		Func<AppDbContext, CancellationToken, Task> seedAsync,
		AppDbContext dbContext) : IInboxSeeder
	{
		private readonly Func<AppDbContext, CancellationToken, Task> _seedAsync = seedAsync;
		private readonly AppDbContext _dbContext = dbContext;

		public Task SeedAsync(CancellationToken ct = default)
		{
			return _seedAsync(_dbContext, ct);
		}
	}

	private sealed class FixedPathTestPlannerDbContextFactory(string dbPath) : IPlannerDbContextFactory
	{
		private readonly string _dbPath = dbPath;

		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
		{
			var options = new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite($"Data Source={_dbPath}")
				.Options;
			return ValueTask.FromResult(new AppDbContext(options));
		}
	}

	private sealed class AttemptCounter
	{
		private int _value;

		public int Value => Volatile.Read(ref _value);

		public int Increment() => Interlocked.Increment(ref _value);
	}
}

