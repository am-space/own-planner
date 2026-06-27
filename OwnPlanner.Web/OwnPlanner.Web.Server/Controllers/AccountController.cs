using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.Account;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Controllers;

/// <summary>
/// Controller for self-service account operations (GDPR data access / portability / erasure).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController : ControllerBase
{
	private readonly IAccountDeletionService _accountDeletionService;
	private readonly IChatSessionManager _chatSessionManager;
	private readonly ILogger<AccountController> _logger;

	public AccountController(
		IAccountDeletionService accountDeletionService,
		IChatSessionManager chatSessionManager,
		ILogger<AccountController> logger)
	{
		_accountDeletionService = accountDeletionService;
		_chatSessionManager = chatSessionManager;
		_logger = logger;
	}

	/// <summary>
	/// Permanently deletes the authenticated user's account and all of their data after verifying
	/// their current password. This action is irreversible.
	/// </summary>
	[HttpPost("delete")]
	public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest? request, CancellationToken cancellationToken)
	{
		if (request is null || string.IsNullOrWhiteSpace(request.Password))
		{
			return BadRequest(new { message = "Password is required." });
		}

		if (!TryGetAuthenticatedUserId(out var userId))
		{
			return Unauthorized(new { message = "Invalid authentication" });
		}

		_logger.LogInformation("Account deletion requested for user {UserId}", userId);

		var result = await _accountDeletionService.DeleteAccountAsync(userId, request.Password, cancellationToken);
		if (!result.Success)
		{
			return BadRequest(new { message = result.ErrorMessage });
		}

		// Drop the in-memory chat session (and its conversation history) for this login, then sign out.
		var sessionId = User.FindFirstValue("SessionId");
		if (!string.IsNullOrWhiteSpace(sessionId))
		{
			await _chatSessionManager.RemoveSessionAsync(sessionId);
		}

		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

		_logger.LogInformation("Account permanently deleted for user {UserId}", userId);
		return Ok(new { message = "Your account and all associated data have been permanently deleted." });
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
