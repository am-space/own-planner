using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Tasks.Recurrence;

namespace OwnPlanner.Mcp.StdioApp;

/// <summary>The stdio host is already bound to one trusted database for its entire lifetime.</summary>
internal sealed class FixedTenantRecurrenceWorker(IServiceScopeFactory scopes, TimeProvider clock,
	ILogger<FixedTenantRecurrenceWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
		do
		{
			try
			{
				using var scope = scopes.CreateScope();
				await scope.ServiceProvider.GetRequiredService<IRecurringTaskLifecycle>().ReconcileAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
			catch (Exception ex) { logger.LogWarning("Recurrence tick failed ({FailureType})", ex.GetType().Name); }
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
