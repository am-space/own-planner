using System.Net;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

/// <summary>One scheduler tick; eligibility and claims live in Application and the per-user transaction.</summary>
public sealed class WeeklyReminderDispatcher(IWeeklyReminderHost host, ILogger<WeeklyReminderDispatcher> logger)
{
	public async Task RunOnceAsync(CancellationToken ct)
	{
		foreach (var user in await host.GetLinkedUsersAsync(ct))
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				await host.WithUserAsync(user, async (service, send) =>
				{
					var claim = await service.ClaimReminderAsync(ct);
					if (claim is null) return;
					try
					{
						await send(claim.Text, ct);
						await service.FinishDeliveryAsync(claim, true, ct: ct);
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch (Exception ex)
					{
						// Only an explicit rate-limit rejection is safely retryable. A timeout or 5xx may have delivered.
						await service.FinishDeliveryAsync(claim, false,
							ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }, ct);
						logger.LogWarning("Weekly reminder delivery did not complete ({FailureType})", ex.GetType().Name);
					}
				}, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception ex) { logger.LogWarning("Weekly reminder processing failed ({FailureType})", ex.GetType().Name); }
		}
	}
}
