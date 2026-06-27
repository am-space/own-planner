using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OwnPlanner.Application.Account;

namespace OwnPlanner.Web.Server.Controllers;

/// <summary>
/// Controller for self-service account data operations (GDPR data access / portability).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController : ControllerBase
{
	private readonly IAccountExportService _accountExportService;
	private readonly ILogger<AccountController> _logger;

	public AccountController(IAccountExportService accountExportService, ILogger<AccountController> logger)
	{
		_accountExportService = accountExportService;
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

		var export = await _accountExportService.CreateExportAsync(cancellationToken);

		// DeleteOnClose removes the temp archive once the response stream has been fully sent.
		var stream = new FileStream(
			export.FilePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			bufferSize: 64 * 1024,
			FileOptions.Asynchronous | FileOptions.DeleteOnClose);

		return File(stream, export.ContentType, export.FileName);
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
