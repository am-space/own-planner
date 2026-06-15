namespace OwnPlanner.Domain.Users;

/// <summary>
/// Repository for per-user usage-limit overrides.
/// </summary>
public interface IUserQuotaOverrideRepository
{
	/// <summary>Gets the override row for the user, or <see langword="null"/> when the user has none.</summary>
	Task<UserQuotaOverride?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
