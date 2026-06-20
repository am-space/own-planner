namespace OwnPlanner.Domain.Users;

/// <summary>
/// Repository interface for PasswordResetToken entity operations.
/// </summary>
public interface IPasswordResetTokenRepository
{
	/// <summary>Returns the active (unconsumed, unexpired) token matching the given hash, or <c>null</c> if none.</summary>
	Task<PasswordResetToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

	/// <summary>Persists a new token and returns the saved entity.</summary>
	Task<PasswordResetToken> AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default);

	/// <summary>Persists changes to an existing token and returns the updated entity.</summary>
	Task<PasswordResetToken> UpdateAsync(PasswordResetToken token, CancellationToken cancellationToken = default);

	/// <summary>Consumes any currently active tokens for the user so only the newest reset link stays valid.</summary>
	Task InvalidateActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
