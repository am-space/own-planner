namespace OwnPlanner.Domain.Users;

/// <summary>
/// Repository interface for PersonalAccessToken entity operations.
/// </summary>
public interface IPersonalAccessTokenRepository
{
	/// <summary>Returns the token with the given id, or <c>null</c> if not found.</summary>
	Task<PersonalAccessToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Returns all tokens owned by the specified user.</summary>
	Task<IReadOnlyList<PersonalAccessToken>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Returns the active (non-revoked) token matching the given hash, or <c>null</c> if none.</summary>
	Task<PersonalAccessToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

	/// <summary>Persists a new token and returns the saved entity.</summary>
	Task<PersonalAccessToken> AddAsync(PersonalAccessToken token, CancellationToken cancellationToken = default);

	/// <summary>Persists changes to an existing token and returns the updated entity.</summary>
	Task<PersonalAccessToken> UpdateAsync(PersonalAccessToken token, CancellationToken cancellationToken = default);
}
