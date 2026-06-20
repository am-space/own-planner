namespace OwnPlanner.Application.Auth;

/// <summary>
/// Service interface for authentication operations.
/// </summary>
public interface IAuthService
{
	/// <summary>
	/// Registers a new user with email, username, and password.
	/// </summary>
	Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Authenticates a user with email/username and password.
	/// </summary>
	Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the current authenticated user by ID.
	/// </summary>
	Task<UserResponse?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the total number of registered users.
	/// </summary>
	Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a new personal access token for the specified user.
	/// </summary>
	Task<PersonalAccessTokenCreatedResponse> CreatePersonalAccessTokenAsync(
		Guid userId,
		CreatePersonalAccessTokenRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists the personal access tokens owned by the specified user.
	/// </summary>
	Task<IReadOnlyList<PersonalAccessTokenResponse>> ListPersonalAccessTokensAsync(
		Guid userId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Revokes a personal access token owned by the specified user.
	/// </summary>
	Task<bool> RevokePersonalAccessTokenAsync(
		Guid userId,
		Guid tokenId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves a bearer token to a planner user identifier.
	/// </summary>
	Task<string?> ResolveMcpBearerTokenUserIdAsync(
		string token,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Initiates a password reset for the given email. If the email maps to an active account,
	/// a single-use reset token is stored and a reset link is emailed. Completes silently
	/// regardless of whether the account exists (anti-enumeration) and never throws.
	/// </summary>
	Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>
	/// Completes a password reset using a previously issued token. On success the user's
	/// password is updated and the token is consumed. Returns a generic failure result for
	/// invalid, expired, or already-used tokens.
	/// </summary>
	Task<AuthResult> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);

	/// <summary>
	/// Verifies a password against a hash.
	/// </summary>
	bool VerifyPassword(string password, string passwordHash);

	/// <summary>
	/// Hashes a password.
	/// </summary>
	string HashPassword(string password);
}
