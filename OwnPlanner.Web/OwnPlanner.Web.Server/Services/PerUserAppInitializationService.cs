using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Mcp.Tools;
using System.Collections.Concurrent;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Ensures each user's planner database is migrated and seeded once per web-server process.
/// The lazy single-flight guard prevents duplicate startup work when multiple sessions for the
/// same user trigger tool calls at the same time.
/// </summary>
public sealed class PerUserAppInitializationService(
	IServiceScopeFactory scopeFactory,
	IPlannerSessionContextAccessor sessionContextAccessor,
	ILogger<PerUserAppInitializationService> logger)
{
	private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
	private readonly IPlannerSessionContextAccessor _sessionContextAccessor = sessionContextAccessor;
	private readonly ILogger<PerUserAppInitializationService> _logger = logger;
	private readonly ConcurrentDictionary<string, Lazy<Task>> _initializations = new(StringComparer.Ordinal);

	public async Task EnsureInitializedAsync(SessionContext sessionContext, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sessionContext);
		if (string.IsNullOrWhiteSpace(sessionContext.UserId))
		{
			throw new UnauthorizedAccessException("Authenticated user id is required for planner tool access.");
		}

		var lazyInitialization = _initializations.GetOrAdd(
			sessionContext.UserId,
			_ => CreateInitializationLazy(sessionContext));

		try
		{
			await lazyInitialization.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			if (lazyInitialization.IsValueCreated)
			{
				var initializationTask = lazyInitialization.Value;
				if (initializationTask.IsFaulted || initializationTask.IsCanceled)
				{
					_initializations.TryRemove(new KeyValuePair<string, Lazy<Task>>(sessionContext.UserId, lazyInitialization));
				}
			}

			throw;
		}
	}

	private Lazy<Task> CreateInitializationLazy(SessionContext sessionContext)
	{
		Lazy<Task> lazyInitialization = null!;
		lazyInitialization = new Lazy<Task>(() => CreateInitializationTask(sessionContext, lazyInitialization!), LazyThreadSafetyMode.ExecutionAndPublication);
		return lazyInitialization;
	}

	private Task CreateInitializationTask(SessionContext sessionContext, Lazy<Task> lazyInitialization)
	{
		var initializationTask = InitializeUserAsync(sessionContext);
		var removalState = new InitializationRemovalState(_initializations, sessionContext.UserId, lazyInitialization);

		_ = initializationTask.ContinueWith(
			static (task, state) =>
			{
				if (!task.IsFaulted && !task.IsCanceled)
				{
					return;
				}

				var removalState = (InitializationRemovalState)state!;
				removalState.Initializations.TryRemove(new KeyValuePair<string, Lazy<Task>>(removalState.UserId, removalState.LazyInitialization));
			},
			removalState,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);

		return initializationTask;
	}

	private async Task InitializeUserAsync(SessionContext sessionContext)
	{
		using var _ = _sessionContextAccessor.BeginScope(sessionContext);
		using var scope = _scopeFactory.CreateScope();
		var serviceProvider = scope.ServiceProvider;
		var dbContextFactory = serviceProvider.GetRequiredService<IPlannerDbContextFactory>();
		await using var dbContext = await dbContextFactory.CreateAsync().ConfigureAwait(false);
		var inboxSeeder = serviceProvider.GetRequiredService<IInboxSeeder>();

		_logger.LogInformation("Initializing planner database for user {UserId}", sessionContext.UserId);
		await dbContext.Database.MigrateAsync().ConfigureAwait(false);
		await inboxSeeder.SeedAsync().ConfigureAwait(false);
		_logger.LogInformation("Planner database ready for user {UserId}", sessionContext.UserId);
	}

	private sealed record InitializationRemovalState(
		ConcurrentDictionary<string, Lazy<Task>> Initializations,
		string UserId,
		Lazy<Task> LazyInitialization);
}

