using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Mcp.Tools;
using System.Collections.Concurrent;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Ensures each user's planner database is migrated and seeded once per web-server process.
/// A single-flight guard prevents duplicate startup work when multiple sessions for the
/// same user trigger tool calls at the same time.
/// <para>
/// Each user gets at most one in-flight or completed initialization entry in the dictionary.
/// A caller whose <c>cancellationToken</c> fires cancels only its own wait via
/// <see cref="Task.WaitAsync(CancellationToken)"/>; the shared initialization task keeps
/// running independently. If that task later faults or is cancelled the failed entry is
/// evicted so the next call retries against a fresh initialization task.
/// </para>
/// </summary>
public sealed class PerUserAppInitializationService(
	IServiceScopeFactory scopeFactory,
	IPlannerSessionContextAccessor sessionContextAccessor,
	ILogger<PerUserAppInitializationService> logger)
{
	// TODO: _initializations retains a completed entry for every user ever seen by this process.
	// In a long-running deployment with many users this can grow without bound.
	// Consider evicting successful entries after a TTL or switching to a bounded cache
	// (e.g., IMemoryCache with sliding expiration) while preserving single-flight behavior.
	private readonly ConcurrentDictionary<string, Lazy<Task>> _initializations = new(StringComparer.Ordinal);

	public async Task EnsureInitializedAsync(SessionContext sessionContext, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sessionContext);
		if (string.IsNullOrWhiteSpace(sessionContext.UserId))
		{
			throw new UnauthorizedAccessException("Authenticated user id is required for planner tool access.");
		}

		while (true)
		{
			// Store the lazy wrapper instead of an already-started task. This lets us distinguish
			// a stale, previously failed entry from a freshly created attempt for this caller.
			var newInitialization = CreateInitialization(sessionContext);
			var initialization = _initializations.GetOrAdd(sessionContext.UserId, newInitialization);

			// If we found an existing entry that has already faulted or been cancelled, evict it
			// and retry immediately so this call can start a fresh initialization attempt.
			if (!ReferenceEquals(initialization, newInitialization) &&
				initialization.IsValueCreated &&
				(initialization.Value.IsFaulted || initialization.Value.IsCanceled))
			{
				_initializations.TryRemove(new KeyValuePair<string, Lazy<Task>>(sessionContext.UserId, initialization));
				continue;
			}

			var initializationTask = initialization.Value;

			try
			{
				// WaitAsync only cancels this caller's wait; the shared task continues unaffected.
				await initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
				return;
			}
			catch
			{
				if (initializationTask.IsFaulted || initializationTask.IsCanceled)
				{
					_initializations.TryRemove(new KeyValuePair<string, Lazy<Task>>(sessionContext.UserId, initialization));
				}

				throw;
			}
		}
	}

	private Lazy<Task> CreateInitialization(SessionContext sessionContext)
	{
		Lazy<Task>? lazyInitialization = null;
		lazyInitialization = new Lazy<Task>(
			() =>
			{
				var initializationTask = InitializeUserAsync(sessionContext);

				_ = initializationTask.ContinueWith(
					static (completedTask, state) =>
					{
						var (initializations, userId, initialization) = ((ConcurrentDictionary<string, Lazy<Task>> Initializations, string UserId, Lazy<Task> Initialization))state!;
						if (completedTask.IsFaulted || completedTask.IsCanceled)
						{
							initializations.TryRemove(new KeyValuePair<string, Lazy<Task>>(userId, initialization));
						}
					},
					(_initializations, sessionContext.UserId, lazyInitialization!),
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);

				return initializationTask;
			},
			LazyThreadSafetyMode.ExecutionAndPublication);

		return lazyInitialization;
	}

	private async Task InitializeUserAsync(SessionContext sessionContext)
	{
		using var _ = sessionContextAccessor.BeginScope(sessionContext);
		using var scope = scopeFactory.CreateScope();
		var serviceProvider = scope.ServiceProvider;
		var dbContextFactory = serviceProvider.GetRequiredService<IPlannerDbContextFactory>();
		await using var dbContext = await dbContextFactory.CreateAsync().ConfigureAwait(false);
		var inboxSeeder = serviceProvider.GetRequiredService<IInboxSeeder>();

		logger.LogInformation("Initializing planner database for user {UserId}", sessionContext.UserId);
		await dbContext.Database.MigrateAsync().ConfigureAwait(false);
		await inboxSeeder.SeedAsync().ConfigureAwait(false);
		logger.LogInformation("Planner database ready for user {UserId}", sessionContext.UserId);
	}
}

