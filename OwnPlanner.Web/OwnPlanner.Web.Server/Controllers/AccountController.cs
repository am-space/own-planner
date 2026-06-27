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
	private readonly IAccountExportService _accountExportService;
	private readonly IAccountDeletionService _accountDeletionService;
	private readonly IPerUserAppInitializationService _initializationService;
	private readonly IChatSessionManager _chatSessionManager;
	private readonly ILogger<AccountController> _logger;

	public AccountController(
		IAccountExportService accountExportService,
		IAccountDeletionService accountDeletionService,
		IPerUserAppInitializationService initializationService,
		IChatSessionManager chatSessionManager,
		ILogger<AccountController> logger)
	{
		_accountExportService = accountExportService;
		_accountDeletionService = accountDeletionService;
		_initializationService = initializationService;
		_chatSessionManager = chatSessionManager;
		_logger = logger;
	}

	/// <summary>
	/// Builds and streams a ZIP export of the authenticated user's planning data.
	/// </summary>
	// The export contains the user's full personal data set; never let a browser or intermediary
	// proxy cache it.
	[HttpGet("export")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public async Task<IActionResult> ExportData(CancellationToken cancellationToken)
	{
		if (!TryGetAuthenticatedUserId(out var userId))
		{
			return Unauthorized(new { message = "Invalid authentication" });
		}

		_logger.LogInformation("Building account data export for user {UserId}", userId);

		// The per-user planner database is created/migrated lazily on first tool use, so a user who
		// exports before ever chatting would otherwise get an empty, schemaless file. Ensure the
		// database is migrated and seeded before snapshotting it.
		await _initializationService.EnsureInitializedAsync(
			User.GetRequiredPlannerSessionContext("export"),
			cancellationToken);

		var export = await _accountExportService.CreateExportAsync(cancellationToken);

		// DeleteOnClose removes the temp archive once the response stream has been fully sent.
		FileStream stream;
		try
		{
			stream = new FileStream(
				export.FilePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				bufferSize: 64 * 1024,
				FileOptions.Asynchronous | FileOptions.DeleteOnClose);
		}
		catch
		{
			// Opening failed, so DeleteOnClose never took ownership of the archive. Delete it now;
			// anything that still slips through is reaped by ExportTempFileCleanupService.
			try
			{
				System.IO.File.Delete(export.FilePath);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.LogWarning(ex, "Failed to delete export archive {Path} after stream open failed", export.FilePath);
			}

			throw;
		}

		return File(stream, export.ContentType, export.FileName);
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
