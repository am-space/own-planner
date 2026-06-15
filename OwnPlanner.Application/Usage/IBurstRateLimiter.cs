namespace OwnPlanner.Application.Usage;

/// <summary>
/// In-memory, per-user sliding-window burst limiter. State is process-local and ephemeral by design — the
/// durable daily quota lives in the database; the burst window only needs to span the last minute.
/// </summary>
public interface IBurstRateLimiter
{
	/// <summary>
	/// Attempts to record one request for the user within the sliding one-minute window.
	/// </summary>
	/// <param name="limitPerMinute">Maximum requests allowed in the window. A value &lt;= 0 disables the check (always allows).</param>
	/// <param name="now">The current time (injected for testability).</param>
	/// <param name="retryAfterSeconds">When the call is rejected, the seconds until the oldest in-window request ages out.</param>
	/// <returns><see langword="true"/> if the request is allowed (and was recorded); <see langword="false"/> if it would exceed the limit.</returns>
	bool TryAcquire(Guid userId, int limitPerMinute, DateTimeOffset now, out int retryAfterSeconds);
}
