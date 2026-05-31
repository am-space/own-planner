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
/// Each user gets at most one in-flight or completed initialization task in the dictionary.
/// A caller whose <paramref name="cancellationToken"/> fires cancels only its own wait via
/// <see cref="Task.WaitAsync(CancellationToken)"/>; the shared initialization task keeps
/// running independently. If that task later faults or is cancelled the caller that first
/// observes the failure removes it from the dictionary so the next call retries.
/// </para>
/// </summary>
public sealed class PerUserAppInitializationService(
	IServiceScopeFactory scopeFactory,
	IPlannerSessionContextAccessor sessionContextAccessor,
	ILogger<PerUserAppInitializationService> logger)
{
	private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
	private readonly IPlannerSessionContextAccessor _sessionContextAccessor = sessionContextAccessor;
	private readonly ILogger<PerUserAppInitializationService> _logger = logger;
	private readonly ConcurrentDictionary<string, Task> _initializations = new(StringComparer.Ordinal);

	public async Task EnsureInitializedAsync(SessionContext sessionContext, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sessionContext);
		if (string.IsNullOrWhiteSpace(sessionContext.UserId))
		{
			throw new UnauthorizedAccessException("Authenticated user id is required for planner tool access.");
		}

		// GetOrAdd guarantees that only one InitializeUserAsync task is started per user.
		// The factory delegate is not guaranteed to run exactly once under contention, but
		// ConcurrentDictionary ensures only one value is stored, so at most one extra task
		// may be started and immediately discarded.
		var initializationTask = _initializations.GetOrAdd(
			sessionContext.UserId,
			_ => InitializeUserAsync(sessionContext));

		try
		{
			// WaitAsync only cancels this caller's wait; the shared task continues unaffected.
			await initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			// If the shared initialization task itself faulted or was cancelled, remove it so
			// the next caller gets a fresh attempt. Use the exact key/value pair to avoid
			// accidentally removing a replacement task inserted by a concurrent caller.
			if (initializationTask.IsFaulted || initializationTask.IsCanceled)
			{
				_initializations.TryRemove(new KeyValuePair<string, Task>(sessionContext.UserId, initializationTask));
			}

			throw;
		}
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
}

