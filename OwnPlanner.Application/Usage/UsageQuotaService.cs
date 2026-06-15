using Microsoft.Extensions.Logging;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Usage;

/// <summary>
/// Coordinates the per-user usage limits: a sliding one-minute burst window (in memory) and a durable daily
/// request quota (database), with optional per-user overrides. Token usage is recorded as a backstop only.
/// Registered scoped (it uses the per-request repositories); the burst window lives in the singleton
/// <see cref="IBurstRateLimiter"/>.
/// </summary>
public sealed class UsageQuotaService(
	IUserDailyUsageRepository dailyUsageRepository,
	IUserQuotaOverrideRepository overrideRepository,
	IBurstRateLimiter burstRateLimiter,
	UsageQuotaOptions options,
	ILogger<UsageQuotaService> logger) : IUsageQuotaService
{
	public async Task<UsageStatus> CheckAndReserveAsync(string userId, CancellationToken cancellationToken = default)
	{
		var id = ParseUserId(userId);
		var now = DateTimeOffset.UtcNow;
		var today = DateOnly.FromDateTime(now.UtcDateTime);
		var resetAtUtc = NextUtcMidnight(now);

		if (!options.Enabled)
		{
			// Not enforced: there is no finite "remaining" to report, so surface it as unlimited (null).
			return new UsageStatus(options.DailyRequestLimit, 0, null, resetAtUtc);
		}

		var (dailyLimit, burstLimit) = await ResolveLimitsAsync(id, cancellationToken).ConfigureAwait(false);

		// Burst check first: it is in-memory and cheap, and a burst-rejected request never touches the DB.
		if (!burstRateLimiter.TryAcquire(id, burstLimit, now, out var retryAfterSeconds))
		{
			logger.LogInformation("Burst limit ({Limit}/min) reached for user {UserId}", burstLimit, id);
			// remaining is null: the daily quota is not the limiting factor and is not read on the burst path.
			throw new UsageQuotaExceededException(UsageLimitKind.Burst, retryAfterSeconds, null, resetAtUtc);
		}

		// Reserve the daily slot atomically. The counter is incremented even when over the limit (no refund).
		var used = await dailyUsageRepository.IncrementRequestAsync(id, today, cancellationToken).ConfigureAwait(false);

		if (dailyLimit > 0 && used > dailyLimit)
		{
			var retryAfter = Math.Max(1, (int)Math.Ceiling((resetAtUtc - now).TotalSeconds));
			logger.LogInformation("Daily limit ({Limit}) reached for user {UserId} (used {Used})", dailyLimit, id, used);
			throw new UsageQuotaExceededException(UsageLimitKind.Daily, retryAfter, 0, resetAtUtc);
		}

		var remaining = dailyLimit > 0 ? Math.Max(0, dailyLimit - used) : (int?)null;
		return new UsageStatus(dailyLimit, used, remaining, resetAtUtc);
	}

	public async Task RecordTokensAsync(string userId, long inputTokens, long outputTokens, CancellationToken cancellationToken = default)
	{
		if (inputTokens <= 0 && outputTokens <= 0)
		{
			return;
		}

		var id = ParseUserId(userId);
		var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
		await dailyUsageRepository.AddTokensAsync(id, today, Math.Max(0, inputTokens), Math.Max(0, outputTokens), cancellationToken).ConfigureAwait(false);
	}

	public async Task<UsageStatus> GetStatusAsync(string userId, CancellationToken cancellationToken = default)
	{
		var id = ParseUserId(userId);
		var now = DateTimeOffset.UtcNow;
		var today = DateOnly.FromDateTime(now.UtcDateTime);
		var resetAtUtc = NextUtcMidnight(now);

		var (dailyLimit, _) = await ResolveLimitsAsync(id, cancellationToken).ConfigureAwait(false);
		var usage = await dailyUsageRepository.GetAsync(id, today, cancellationToken).ConfigureAwait(false);
		var used = usage?.RequestCount ?? 0;
		var remaining = dailyLimit > 0 ? Math.Max(0, dailyLimit - used) : (int?)null;

		return new UsageStatus(dailyLimit, used, remaining, resetAtUtc);
	}

	private async Task<(int DailyLimit, int BurstLimit)> ResolveLimitsAsync(Guid userId, CancellationToken cancellationToken)
	{
		var userOverride = await overrideRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
		var dailyLimit = userOverride?.DailyRequestLimit ?? options.DailyRequestLimit;
		var burstLimit = userOverride?.BurstRequestsPerMinute ?? options.BurstRequestsPerMinute;
		return (dailyLimit, burstLimit);
	}

	private static DateTimeOffset NextUtcMidnight(DateTimeOffset now)
		=> new(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

	private static Guid ParseUserId(string userId)
		=> Guid.TryParse(userId, out var id)
			? id
			: throw new ArgumentException($"User id '{userId}' is not a valid identifier.", nameof(userId));
}
