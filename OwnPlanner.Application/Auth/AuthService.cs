using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Auth.DTOs;
using OwnPlanner.Application.Auth.Interfaces;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Auth;

/// <summary>
/// Service for handling authentication operations including registration, login, and password management.
/// Uses BCrypt for password hashing.
/// </summary>
public class AuthService : IAuthService
{
	private readonly IUserRepository _userRepository;
	private readonly ILogger<AuthService> _logger;

	public AuthService(IUserRepository userRepository, ILogger<AuthService> logger)
	{
		_userRepository = userRepository;
		_logger = logger;
	}

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
			if (await _userRepository.ExistsByEmailAsync(request.Email, cancellationToken))
			{
				_logger.LogWarning("Registration attempt with existing email: {Email}", request.Email);
				return new AuthResult(false, "Email is already registered");
			}

			// Hash password
			var passwordHash = HashPassword(request.Password);

			// Create user
			var user = new User(request.Email, request.Username, passwordHash);
			user = await _userRepository.AddAsync(user, cancellationToken);

			_logger.LogInformation("User registered successfully: {UserId}, {Email}", user.Id, user.Email);

			var userResponse = MapToUserResponse(user);
			return new AuthResult(true, User: userResponse);
		}
		catch (ArgumentException ex)
		{
			_logger.LogWarning(ex, "Invalid registration data");
			return new AuthResult(false, ex.Message);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error during user registration");
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
			var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

			if (user == null)
			{
				_logger.LogWarning("Login attempt with non-existent email: {Email}", request.Email);
				return new AuthResult(false, "Invalid email or password");
			}

			if (!user.IsActive)
			{
				_logger.LogWarning("Login attempt for deactivated user: {UserId}", user.Id);
				return new AuthResult(false, "Account is deactivated");
			}

			// Verify password
			if (!VerifyPassword(request.Password, user.PasswordHash))
			{
				_logger.LogWarning("Failed login attempt for user: {UserId}", user.Id);
				return new AuthResult(false, "Invalid email or password");
			}

			// Update last login time
			user.RecordLogin();
			await _userRepository.UpdateAsync(user, cancellationToken);

			_logger.LogInformation("User logged in successfully: {UserId}", user.Id);

			var userResponse = MapToUserResponse(user);
			return new AuthResult(true, User: userResponse);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error during user login");
			return new AuthResult(false, "An error occurred during login");
		}
	}

	public async Task<UserResponse?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
		return user != null ? MapToUserResponse(user) : null;
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
}
