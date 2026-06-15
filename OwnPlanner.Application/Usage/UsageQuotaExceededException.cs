namespace OwnPlanner.Application.Usage;

/// <summary>Identifies which usage limit was breached.</summary>
public enum UsageLimitKind
{
	Daily,
	Burst,
}

/// <summary>
/// Thrown when a user's request would exceed a usage limit. Carries everything the HTTP layer needs to
/// return a 429 response: which limit was hit, a <c>Retry-After</c> value, remaining quota, and the reset time.
/// </summary>
public sealed class UsageQuotaExceededException(
	UsageLimitKind limitKind,
	int retryAfterSeconds,
	int? remaining,
	DateTimeOffset resetAtUtc)
	: Exception(BuildMessage(limitKind, resetAtUtc))
{
	public UsageLimitKind LimitKind { get; } = limitKind;
	public int RetryAfterSeconds { get; } = retryAfterSeconds;

	/// <summary>
	/// Remaining daily requests, or <see langword="null"/> when not applicable — e.g. a burst rejection,
	/// where the daily quota is not the limiting factor and is not read on the throttle path.
	/// </summary>
	public int? Remaining { get; } = remaining;
	public DateTimeOffset ResetAtUtc { get; } = resetAtUtc;

	private static string BuildMessage(UsageLimitKind limitKind, DateTimeOffset resetAtUtc) => limitKind switch
	{
		UsageLimitKind.Daily => $"Daily request limit reached. Resets at {resetAtUtc.UtcDateTime:HH:mm} UTC.",
		UsageLimitKind.Burst => "Too many requests in a short period. Please slow down and try again shortly.",
		_ => "Usage limit reached.",
	};
}
