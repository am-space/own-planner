namespace OwnPlanner.Application.Usage;

/// <summary>
/// Enforces per-user request quotas before a chat request reaches Gemini and records token usage afterwards.
/// </summary>
public interface IUsageQuotaService
{
	/// <summary>
	/// Checks the user's burst and daily limits and, when allowed, atomically reserves (increments) the
	/// daily request counter. The increment happens up-front and is never refunded, so a request that later
	/// fails still consumes quota (preventing retry-loop bypass).
	/// </summary>
	/// <exception cref="UsageQuotaExceededException">Thrown when a burst or daily limit would be exceeded.</exception>
	/// <returns>The user's daily quota status after reserving this request.</returns>
	Task<UsageStatus> CheckAndReserveAsync(string userId, CancellationToken cancellationToken = default);

	/// <summary>Records Gemini token usage for a completed chat turn (backstop accounting; not enforced).</summary>
	Task RecordTokensAsync(string userId, long inputTokens, long outputTokens, CancellationToken cancellationToken = default);

	/// <summary>Reads the user's current daily quota status without reserving a request.</summary>
	Task<UsageStatus> GetStatusAsync(string userId, CancellationToken cancellationToken = default);
}
