namespace OwnPlanner.Domain.Users;

/// <summary>
/// Per-user override of the configured usage limits. A <see langword="null"/> value means "fall back
/// to the application default" for that limit. Lets specific users (testers, yourself, or future paid
/// tiers) run with limits different from the appsettings defaults without code changes.
/// </summary>
public class UserQuotaOverride : EntityBase
{
	public Guid UserId { get; private set; }

	/// <summary>Overrides the daily request quota; <see langword="null"/> uses the configured default.</summary>
	public int? DailyRequestLimit { get; private set; }

	/// <summary>Overrides the per-minute burst limit; <see langword="null"/> uses the configured default.</summary>
	public int? BurstRequestsPerMinute { get; private set; }

	// EF Core constructor
	private UserQuotaOverride() { }

	public UserQuotaOverride(Guid userId, int? dailyRequestLimit = null, int? burstRequestsPerMinute = null)
		: base(Guid.NewGuid())
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		UserId = userId;
		SetLimits(dailyRequestLimit, burstRequestsPerMinute);
	}

	public void SetLimits(int? dailyRequestLimit, int? burstRequestsPerMinute)
	{
		if (dailyRequestLimit is < 0)
			throw new ArgumentOutOfRangeException(nameof(dailyRequestLimit), "Daily request limit cannot be negative.");
		if (burstRequestsPerMinute is < 0)
			throw new ArgumentOutOfRangeException(nameof(burstRequestsPerMinute), "Burst request limit cannot be negative.");

		DailyRequestLimit = dailyRequestLimit;
		BurstRequestsPerMinute = burstRequestsPerMinute;
		Touch();
	}
}
