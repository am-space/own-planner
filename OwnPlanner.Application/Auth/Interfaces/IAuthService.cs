using OwnPlanner.Application.Auth.DTOs;

namespace OwnPlanner.Application.Auth.Interfaces;

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
	/// Verifies a password against a hash.
	/// </summary>
	bool VerifyPassword(string password, string passwordHash);

	/// <summary>
	/// Hashes a password.
	/// </summary>
	string HashPassword(string password);
}
