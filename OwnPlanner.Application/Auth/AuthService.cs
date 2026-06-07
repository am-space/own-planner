using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Auth;

/// <summary>
/// Service for handling authentication operations including registration, login, and password management.
/// Uses BCrypt for password hashing.
/// </summary>
public class AuthService(
	IUserRepository userRepository,
	IPersonalAccessTokenRepository personalAccessTokenRepository,
	ILogger<AuthService> logger)
	: IAuthService
{
	public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
	{
		try
		{
			// Validate request
			if (string.IsNullOrWhiteSpace(request.Email))
				return new AuthResult(false, "Email is required");

			if (string.IsNullOrWhiteSpace(request.Username))
				return new AuthResult(false, "Username is required");

			if (string.IsNullOrWhiteSpace(request.Password))
				return new AuthResult(false, "Password is required");

			if (request.Password.Length < 8)
				return new AuthResult(false, "Password must be at least 8 characters");

			// Check if email already exists
			if (await userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
			{
				logger.LogWarning("Registration attempt with existing email: {Email}", request.Email);
				return new AuthResult(false, "Email is already registered");
			}

			// Hash password
			var passwordHash = HashPassword(request.Password);

			// Create user
			var user = new User(request.Email, request.Username, passwordHash);
			user = await userRepository.AddAsync(user, cancellationToken);

			logger.LogInformation("User registered successfully: {UserId}, {Email}", user.Id, user.Email);

			var userResponse = MapToUserResponse(user);
			return new AuthResult(true, User: userResponse);
		}
		catch (ArgumentException ex)
		{
			logger.LogWarning(ex, "Invalid registration data");
			return new AuthResult(false, ex.Message);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during user registration");
			return new AuthResult(false, "An error occurred during registration");
		}
	}

	public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(request.Email))
				return new AuthResult(false, "Email is required");

			if (string.IsNullOrWhiteSpace(request.Password))
				return new AuthResult(false, "Password is required");

			// Find user by email
			var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

			if (user == null)
			{
				logger.LogWarning("Login attempt with non-existent email: {Email}", request.Email);
				return new AuthResult(false, "Invalid email or password");
			}

			if (!user.IsActive)
			{
				logger.LogWarning("Login attempt for deactivated user: {UserId}", user.Id);
				return new AuthResult(false, "Account is deactivated");
			}

			// Verify password
			if (!VerifyPassword(request.Password, user.PasswordHash))
			{
				logger.LogWarning("Failed login attempt for user: {UserId}", user.Id);
				return new AuthResult(false, "Invalid email or password");
			}

			// Update last login time
			user.RecordLogin();
			await userRepository.UpdateAsync(user, cancellationToken);

			logger.LogInformation("User logged in successfully: {UserId}", user.Id);

			var userResponse = MapToUserResponse(user);
			return new AuthResult(true, User: userResponse);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during user login");
			return new AuthResult(false, "An error occurred during login");
		}
	}

	public async Task<UserResponse?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await userRepository.GetByIdAsync(userId, cancellationToken);
		return user != null ? MapToUserResponse(user) : null;
	}

	public async Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default)
	{
		return await userRepository.GetRegisteredUserCountAsync(cancellationToken);
	}

	public async Task<PersonalAccessTokenCreatedResponse> CreatePersonalAccessTokenAsync(
		Guid userId,
		CreatePersonalAccessTokenRequest request,
		CancellationToken cancellationToken = default)
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		if (string.IsNullOrWhiteSpace(request.Name))
			throw new ArgumentException("Token name is required", nameof(request));

		var plaintextToken = GeneratePlaintextToken();
		var tokenHash = HashToken(plaintextToken);

		var token = new PersonalAccessToken(userId, request.Name, tokenHash);
		token = await personalAccessTokenRepository.AddAsync(token, cancellationToken);

		logger.LogInformation("Created personal access token {TokenId} for user {UserId}", token.Id, userId);

		return new PersonalAccessTokenCreatedResponse(
			MapToTokenResponse(token),
			plaintextToken);
	}

	public async Task<IReadOnlyList<PersonalAccessTokenResponse>> ListPersonalAccessTokensAsync(
		Guid userId,
		CancellationToken cancellationToken = default)
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		var tokens = await personalAccessTokenRepository.ListByUserIdAsync(userId, cancellationToken);
		return tokens
			.OrderByDescending(token => token.CreatedAt)
			.Select(MapToTokenResponse)
			.ToList();
	}

	public async Task<bool> RevokePersonalAccessTokenAsync(
		Guid userId,
		Guid tokenId,
		CancellationToken cancellationToken = default)
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		if (tokenId == Guid.Empty)
			throw new ArgumentException("Token id is required", nameof(tokenId));

		var token = await personalAccessTokenRepository.GetByIdAsync(tokenId, cancellationToken);
		if (token is null || token.UserId != userId)
		{
			return false;
		}

		if (token.RevokedAt is null)
		{
			token.Revoke();
			await personalAccessTokenRepository.UpdateAsync(token, cancellationToken);
		}

		logger.LogInformation("Revoked personal access token {TokenId} for user {UserId}", tokenId, userId);
		return true;
	}

	private static readonly TimeSpan LastUsedUpdateThreshold = TimeSpan.FromMinutes(1);

	public async Task<string?> ResolveMcpBearerTokenUserIdAsync(
		string token,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		var tokenHash = HashToken(token);
		var accessToken = await personalAccessTokenRepository.FindActiveByTokenHashAsync(tokenHash, cancellationToken);
		if (accessToken is null)
		{
			return null;
		}

		if (accessToken.LastUsedAt is null ||
			DateTime.UtcNow - accessToken.LastUsedAt.Value >= LastUsedUpdateThreshold)
		{
			accessToken.RecordUsage();
			await personalAccessTokenRepository.UpdateAsync(accessToken, cancellationToken);
		}

		return accessToken.UserId.ToString();
	}

	public string HashPassword(string password)
	{
		// Using BCrypt with work factor of 12 (good balance between security and performance)
		return BCrypt.Net.BCrypt.HashPassword(password, 12);
	}

	public bool VerifyPassword(string password, string passwordHash)
	{
		try
		{
			return BCrypt.Net.BCrypt.Verify(password, passwordHash);
		}
		catch
		{
			return false;
		}
	}

	private static string GeneratePlaintextToken()
	{
		var bytes = RandomNumberGenerator.GetBytes(32);
		return $"opat_{Convert.ToHexString(bytes).ToLowerInvariant()}";
	}

	private static string HashToken(string token)
		=> Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

	private static UserResponse MapToUserResponse(User user)
	{
		return new UserResponse(
			user.Id,
			user.Email,
			user.Username,
			user.CreatedAt,
			user.LastLoginAt
		);
	}

	private static PersonalAccessTokenResponse MapToTokenResponse(PersonalAccessToken token)
	{
		return new PersonalAccessTokenResponse(
			token.Id,
			token.UserId,
			token.Name,
			token.CreatedAt,
			token.LastUsedAt,
			token.RevokedAt);
	}
}
