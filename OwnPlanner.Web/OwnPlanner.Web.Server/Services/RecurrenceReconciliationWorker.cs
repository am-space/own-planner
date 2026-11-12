using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>Explicit per-account recurrence writes, independent of reminders and request reads.</summary>
public sealed class RecurrenceReconciliationWorker(IServiceScopeFactory scopes,
	IPlannerSessionContextAccessor accessor, IPerUserAppInitializationService initialization,
	TimeProvider clock, ILogger<RecurrenceReconciliationWorker> logger) : BackgroundService
{
	public async Task RunOnceAsync(CancellationToken ct)
	{
		List<Guid> users;
		using (var scope = scopes.CreateScope())
		{
			var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
			users = await auth.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);
		}
		foreach (var user in users)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				using var scope = scopes.CreateScope();
				var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
				if (!await auth.Users.AnyAsync(u => u.Id == user && u.IsActive, ct)) continue;
				var context = new SessionContext { UserId = user.ToString(), SessionId = "recurrence-reconciliation" };
				await initialization.EnsureInitializedAsync(context, ct);
				using var session = accessor.BeginScope(context);
				await scope.ServiceProvider.GetRequiredService<IRecurringTaskLifecycle>().ReconcileAsync(ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				// A broken tenant does not prevent the others catching up; exclude data and paths.
				logger.LogWarning("Recurrence account reconciliation failed ({FailureType})", ex.GetType().Name);
			}
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Central auth migrations complete during host startup before the first minute tick.
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try { await RunOnceAsync(stoppingToken); }
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
			catch (Exception ex) { logger.LogWarning("Recurrence tick failed ({FailureType})", ex.GetType().Name); }
		}
	}
}
