using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.Auth;

namespace OwnPlanner.Web.Server.Controllers;

/// <summary>
/// Controller for authentication operations (register, login, logout).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
	private readonly IAuthService _authService;
	private readonly ILogger<AuthController> _logger;

	public AuthController(IAuthService authService, ILogger<AuthController> logger)
	{
		_authService = authService;
		_logger = logger;
	}

	/// <summary>
	/// Registers a new user account.
	/// </summary>
	[HttpPost("register")]
	[AllowAnonymous]
	public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Registration attempt for email: {Email}", request.Email);

		var result = await _authService.RegisterAsync(request, cancellationToken);

		if (!result.Success)
		{
			_logger.LogWarning("Registration failed for email: {Email}, Reason: {Reason}", request.Email, result.ErrorMessage);
			return BadRequest(new { message = result.ErrorMessage });
		}

		// Automatically log in the user after successful registration
		await SignInUserAsync(result.User!);

		_logger.LogInformation("User registered and logged in: {UserId}", result.User!.Id);
		return Ok(new { message = "Registration successful", user = result.User });
	}

	/// <summary>
	/// Authenticates a user and creates an authentication cookie.
	/// </summary>
	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Login attempt for: {Email}", request.Email);

		var result = await _authService.LoginAsync(request, cancellationToken);

		if (!result.Success)
		{
			_logger.LogWarning("Login failed for: {Email}, Reason: {Reason}", request.Email, result.ErrorMessage);
			return Unauthorized(new { message = result.ErrorMessage });
		}

		await SignInUserAsync(result.User!);

		_logger.LogInformation("User logged in: {UserId}", result.User!.Id);
		return Ok(new { message = "Login successful", user = result.User });
	}

	/// <summary>
	/// Initiates a password reset. Always returns 200 to avoid revealing whether the email is registered.
	/// </summary>
	[HttpPost("forgot-password")]
	[AllowAnonymous]
	public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Password reset requested");

		await _authService.RequestPasswordResetAsync(request.Email, cancellationToken);

		// Anti-enumeration: respond identically regardless of whether the account exists.
		return Ok(new { message = "If an account exists for that email, a password reset link has been sent." });
	}

	/// <summary>
	/// Completes a password reset using a token issued by <see cref="ForgotPassword"/>.
	/// </summary>
	[HttpPost("reset-password")]
	[AllowAnonymous]
	public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
	{
		var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);

		if (!result.Success)
		{
			return BadRequest(new { message = result.ErrorMessage });
		}

		_logger.LogInformation("Password reset completed for user {UserId}", result.User!.Id);
		return Ok(new { message = "Password has been reset successfully." });
	}

	/// <summary>
	/// Logs out the current user and removes the authentication cookie.
	/// </summary>
	[HttpPost("logout")]
	[Authorize]
	public async Task<IActionResult> Logout()
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		var sessionId = User.FindFirstValue("SessionId");
		
		_logger.LogInformation("User logging out - UserId: {UserId}, SessionId: {SessionId}", userId, sessionId);

		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

		return Ok(new { message = "Logout successful" });
	}

	/// <summary>
	/// Gets the currently authenticated user's information.
	/// </summary>
	[HttpGet("me")]
	[Authorize]
	public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
	{
		var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
		
		if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
		{
			_logger.LogWarning("Invalid user ID claim in token");
			return Unauthorized(new { message = "Invalid authentication" });
		}

		var user = await _authService.GetUserByIdAsync(userId, cancellationToken);

		if (user == null)
		{
			_logger.LogWarning("User not found: {UserId}", userId);
			return NotFound(new { message = "User not found" });
		}

		return Ok(user);
	}

	/// <summary>
	/// Checks if the user is authenticated.
	/// </summary>
	[HttpGet("check")]
	public IActionResult CheckAuth()
	{
		if (User.Identity?.IsAuthenticated == true)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var username = User.FindFirstValue(ClaimTypes.Name);
			var email = User.FindFirstValue(ClaimTypes.Email);
			var sessionId = User.FindFirstValue("SessionId");

			return Ok(new
			{
				isAuthenticated = true,
				userId,
				username,
				email,
				sessionId
			});
		}

		return Ok(new { isAuthenticated = false });
	}

	/// <summary>
	/// Gets public authentication-related application statistics.
	/// </summary>
	[HttpGet("stats")]
	[AllowAnonymous]
	public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
	{
		var registeredUserCount = await _authService.GetRegisteredUserCountAsync(cancellationToken);
		return Ok(new { registeredUserCount });
	}

	/// <summary>
	/// Lists the current user's personal access tokens.
	/// </summary>
	[HttpGet("tokens")]
	[Authorize]
	public async Task<IActionResult> ListPersonalAccessTokens(CancellationToken cancellationToken)
	{
		if (!TryGetAuthenticatedUserId(out var userId))
		{
			return Unauthorized(new { message = "Invalid authentication" });
		}

		var tokens = await _authService.ListPersonalAccessTokensAsync(userId, cancellationToken);
		return Ok(tokens);
	}

	/// <summary>
	/// Creates a new personal access token for the current user.
	/// </summary>
	[HttpPost("tokens")]
	[Authorize]
	public async Task<IActionResult> CreatePersonalAccessToken([FromBody] CreatePersonalAccessTokenRequest? request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			return BadRequest(new { message = "Request body is required." });
		}

		if (!TryGetAuthenticatedUserId(out var userId))
		{
			return Unauthorized(new { message = "Invalid authentication" });
		}

		try
		{
			var result = await _authService.CreatePersonalAccessTokenAsync(userId, request, cancellationToken);
			return Ok(new
			{
				token = result.Token,
				plaintextToken = result.PlaintextToken
			});
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { message = ex.Message });
		}
	}

	/// <summary>
	/// Revokes an existing personal access token for the current user.
	/// </summary>
	[HttpDelete("tokens/{tokenId:guid}")]
	[Authorize]
	public async Task<IActionResult> RevokePersonalAccessToken(Guid tokenId, CancellationToken cancellationToken)
	{
		if (!TryGetAuthenticatedUserId(out var userId))
		{
			return Unauthorized(new { message = "Invalid authentication" });
		}

		var revoked = await _authService.RevokePersonalAccessTokenAsync(userId, tokenId, cancellationToken);
		return revoked ? NoContent() : NotFound(new { message = "Personal access token not found" });
	}

	private async Task SignInUserAsync(UserResponse user)
	{
		// Generate a unique session ID for this login
		var sessionId = Guid.NewGuid().ToString();

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new(ClaimTypes.Name, user.Username),
			new(ClaimTypes.Email, user.Email),
			new("SessionId", sessionId) // Unique identifier for this login session
		};

		var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
		var authProperties = new AuthenticationProperties
		{
			IsPersistent = true, // Remember me across browser sessions
			ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7), // Cookie expires in 7 days
			AllowRefresh = true
		};

		await HttpContext.SignInAsync(
			CookieAuthenticationDefaults.AuthenticationScheme,
			new ClaimsPrincipal(claimsIdentity),
			authProperties);

		_logger.LogDebug("Created session ID for user {UserId}: {SessionId}", user.Id, sessionId);
	}

	private bool TryGetAuthenticatedUserId(out Guid userId)
	{
		userId = default;
		var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
		{
			_logger.LogWarning("Invalid user ID claim in token");
			return false;
		}

		return true;
	}
}
