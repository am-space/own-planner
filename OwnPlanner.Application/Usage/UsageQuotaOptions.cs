namespace OwnPlanner.Application.Usage;

/// <summary>
/// Configured usage limits applied to every user unless a per-user <see cref="Domain.Users.UserQuotaOverride"/>
/// supplies a different value. Bound from the "UsageQuota" configuration section.
/// </summary>
public sealed class UsageQuotaOptions
{
	/// <summary>When <see langword="false"/>, request limits are not enforced (tokens are still recorded).</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Maximum chat requests a user may make per UTC day. A value &lt;= 0 disables the daily check.</summary>
	public int DailyRequestLimit { get; set; } = 200;

	/// <summary>Maximum chat requests a user may make within a sliding one-minute window. A value &lt;= 0 disables the burst check.</summary>
	public int BurstRequestsPerMinute { get; set; } = 10;
}
