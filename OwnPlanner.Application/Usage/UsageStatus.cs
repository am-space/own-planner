namespace OwnPlanner.Application.Usage;

/// <summary>
/// A snapshot of a user's daily request quota, suitable for surfacing remaining quota to the client.
/// </summary>
/// <param name="DailyLimit">The effective daily request limit for the user (&lt;= 0 means unlimited).</param>
/// <param name="Used">Requests the user has made so far today (UTC).</param>
/// <param name="Remaining">
/// Requests remaining today (never negative), or <see langword="null"/> when the daily quota is
/// unlimited or enforcement is disabled — i.e. there is no finite "remaining" to report.
/// </param>
/// <param name="ResetAtUtc">When the daily counter resets (the next UTC midnight).</param>
public sealed record UsageStatus(int DailyLimit, int Used, int? Remaining, DateTimeOffset ResetAtUtc);
