namespace OwnPlanner.Domain.Users;

/// <summary>
/// Repository for per-user, per-day usage tracking. Increments are atomic so that concurrent requests
/// from the same user cannot lose updates (the quota race guard).
/// </summary>
public interface IUserDailyUsageRepository
{
	/// <summary>
	/// Atomically creates the (user, date) row if absent and increments its request count, returning the
	/// new count. The whole operation is a single statement so concurrent callers never overwrite each
	/// other's increment.
	/// </summary>
	/// <returns>The request count for the user on that date <em>after</em> this increment.</returns>
	Task<int> IncrementRequestAsync(Guid userId, DateOnly dateUtc, CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomically adds token counts to the (user, date) row, creating the row (with a zero request count) if
	/// it does not already exist. The row is normally created by <see cref="IncrementRequestAsync"/> at request
	/// start, but the upsert also covers the case where enforcement is disabled and no reservation ran.
	/// </summary>
	Task AddTokensAsync(Guid userId, DateOnly dateUtc, long inputTokens, long outputTokens, CancellationToken cancellationToken = default);

	/// <summary>Gets the usage row for the given user and date, or <see langword="null"/> if none exists yet.</summary>
	Task<UserDailyUsage?> GetAsync(Guid userId, DateOnly dateUtc, CancellationToken cancellationToken = default);
}
