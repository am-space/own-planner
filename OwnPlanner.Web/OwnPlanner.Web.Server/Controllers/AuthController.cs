using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.Auth.DTOs;
using OwnPlanner.Application.Auth.Interfaces;
using System.Security.Claims;

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
	/// Logs out the current user and removes the authentication cookie.
	/// </summary>
	[HttpPost("logout")]
	[Authorize]
	public async Task<IActionResult> Logout()
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		_logger.LogInformation("User logging out: {UserId}", userId);

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

			return Ok(new
			{
				isAuthenticated = true,
				userId,
				username,
				email
			});
		}

		return Ok(new { isAuthenticated = false });
	}

	private async Task SignInUserAsync(UserResponse user)
	{
		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, user.Id.ToString()),
			new(ClaimTypes.Name, user.Username),
			new(ClaimTypes.Email, user.Email)
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
	}
}
