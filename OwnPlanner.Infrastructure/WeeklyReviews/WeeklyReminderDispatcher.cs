using System.Net;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Infrastructure.WeeklyReviews;

/// <summary>One scheduler tick; eligibility and claims live in Application and the per-user transaction.</summary>
public sealed class WeeklyReminderDispatcher(IWeeklyReminderHost host, ILogger<WeeklyReminderDispatcher> logger)
{
	internal const int MaximumConcurrentUsers = 4;

	public async Task RunOnceAsync(CancellationToken ct)
	{
		await Parallel.ForEachAsync(await host.GetLinkedUsersAsync(ct),
			new ParallelOptions { MaxDegreeOfParallelism = MaximumConcurrentUsers, CancellationToken = ct },
			async (user, userToken) =>
		{
			try
			{
				await host.WithUserAsync(user, async (service, send) =>
				{
					var claim = await service.ClaimReminderAsync(userToken);
					if (claim is null) return;
					try
					{
						await send(claim.Text, userToken);
						await service.FinishDeliveryAsync(claim, true, ct: userToken);
					}
					catch (OperationCanceledException) when (userToken.IsCancellationRequested) { throw; }
					catch (Exception ex)
					{
						// Only an explicit rate-limit rejection is safely retryable. A timeout or 5xx may have delivered.
						await service.FinishDeliveryAsync(claim, false,
							ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }, userToken);
						logger.LogWarning("Weekly reminder delivery did not complete ({FailureType})", ex.GetType().Name);
					}
				}, userToken);
			}
			catch (OperationCanceledException) when (userToken.IsCancellationRequested) { throw; }
			catch (Exception ex) { logger.LogWarning("Weekly reminder processing failed ({FailureType})", ex.GetType().Name); }
		});
	}
}
