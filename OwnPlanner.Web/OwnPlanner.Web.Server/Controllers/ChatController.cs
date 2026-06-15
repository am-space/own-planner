using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Usage;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Controllers
{
	/// <summary>
	/// Controller for AI chat functionality with per-login session management
	/// </summary>
	[ApiController]
	[Route("api/[controller]")]
	[Authorize]
	public class ChatController : ControllerBase
	{
		private readonly IChatSessionManager _sessionManager;
		private readonly IUsageQuotaService _usageQuotaService;
		private readonly ILogger<ChatController> _logger;
		private readonly int _defaultMaxContextLengthTokens;

		public ChatController(
			IChatSessionManager sessionManager,
			IUsageQuotaService usageQuotaService,
			ILogger<ChatController> logger,
			IOptions<ChatSettings> chatSettings)
		{
			_sessionManager = sessionManager;
			_usageQuotaService = usageQuotaService;
			_logger = logger;
			_defaultMaxContextLengthTokens = chatSettings.Value.Gemini.MaxContextLengthTokens;
		}

		/// <summary>
		/// Send a message to the chat and get a response
		/// </summary>
		[HttpPost("message")]
		public async Task<IActionResult> SendMessage([FromBody] ChatRequest request, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(request.Message))
			{
				return BadRequest(new { message = "Message cannot be empty" });
			}

			var sessionId = GetSessionId();
			var userId = GetUserId();
			_logger.LogInformation("Processing chat message for sessionId: {SessionId}, userId: {UserId}", sessionId, userId);

			UsageStatus usage;
			try
			{
				usage = await _usageQuotaService.CheckAndReserveAsync(userId, cancellationToken);
			}
			catch (UsageQuotaExceededException ex)
			{
				_logger.LogInformation(
					"Usage quota ({LimitKind}) reached for sessionId: {SessionId}, userId: {UserId}",
					ex.LimitKind, sessionId, userId);
				Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
				return StatusCode(StatusCodes.Status429TooManyRequests, new
				{
					message = ex.Message,
					limitKind = ex.LimitKind.ToString(),
					remainingDailyQuota = ex.Remaining,
					quotaResetAtUtc = ex.ResetAtUtc,
					retryAfterSeconds = ex.RetryAfterSeconds,
				});
			}

			try
			{
				var chatService = await _sessionManager.GetOrCreateSessionAsync(sessionId, userId, cancellationToken);
				var response = await chatService.GetResponseAsync(request.Message, cancellationToken);

				_logger.LogInformation("Chat response generated for sessionId: {SessionId}", sessionId);

				// Backstop token accounting: never let a recording failure fail the user's request.
				await RecordTokenUsageAsync(userId, response.InputTokens, response.OutputTokens, cancellationToken);

				return Ok(new ChatResponse
				{
                 Message = response.Message,
					SessionId = sessionId,
                 Timestamp = DateTime.UtcNow,
                  ContextLengthTokens = response.ContextLengthTokens,
					MaxContextLengthTokens = chatService.MaxContextLengthTokens,
					RemainingDailyQuota = usage.Remaining,
					QuotaResetAtUtc = usage.ResetAtUtc,
				});
			}
            catch (ChatContextLimitExceededException ex)
			{
				_logger.LogInformation(ex, "Chat context limit reached for sessionId: {SessionId}", sessionId);
				return BadRequest(new
				{
					message = ex.Message,
					contextLengthTokens = ex.CurrentContextLengthTokens,
					maxContextLengthTokens = ex.MaxContextLengthTokens,
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing chat message for sessionId: {SessionId}", sessionId);
				return StatusCode(500, new { message = "An error occurred while processing your message" });
			}
		}

		/// <summary>
		/// Switch the planning mode for the current session
		/// </summary>
		[HttpPost("mode")]
		public async Task<IActionResult> SwitchMode([FromBody] SwitchModeRequest request, CancellationToken cancellationToken)
		{
			if (!Enum.TryParse<PlanningMode>(request.Mode, ignoreCase: true, out var mode))
			{
				return BadRequest(new { message = $"Invalid mode '{request.Mode}'. Valid values: {string.Join(", ", Enum.GetNames<PlanningMode>())}" });
			}

			var sessionId = GetSessionId();
			var userId = GetUserId();
			_logger.LogInformation("Switching planning mode to {Mode} for sessionId: {SessionId}", mode, sessionId);

			try
			{
				var chatService = await _sessionManager.GetOrCreateSessionAsync(sessionId, userId, cancellationToken);
				await chatService.SwitchModeAsync(mode, cancellationToken);
				return Ok(new { mode = mode.ToString(), sessionId });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error switching planning mode for sessionId: {SessionId}", sessionId);
				return StatusCode(500, new { message = "An error occurred while switching the mode" });
			}
		}

		/// <summary>
		/// Get starter prompts for a planning mode
		/// </summary>
		[HttpGet("mode/{mode}/prompts")]
		public IActionResult GetModeStarterPrompts(string mode)
		{
			if (!Enum.TryParse<PlanningMode>(mode, ignoreCase: true, out var planningMode))
			{
				return BadRequest(new { message = $"Invalid mode '{mode}'. Valid values: {string.Join(", ", Enum.GetNames<PlanningMode>())}" });
			}

			var config = ModeConfig.All[planningMode];
			return Ok(new ModeStarterPromptsResponse
			{
				Mode = planningMode.ToString(),
				StarterPrompts = config.StarterPrompts,
			});
		}

		/// <summary>
		/// Clear the current user's chat session (start a new conversation)
		/// </summary>
		[HttpPost("clear")]
		public async Task<IActionResult> ClearSession()
		{
			var sessionId = GetSessionId();
			_logger.LogInformation("Clearing chat session: {SessionId}", sessionId);

			try
			{
				await _sessionManager.RemoveSessionAsync(sessionId);
				return Ok(new { message = "Chat session cleared", sessionId });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error clearing chat session: {SessionId}", sessionId);
				return StatusCode(500, new { message = "An error occurred while clearing the session" });
			}
		}

		/// <summary>
		/// Get the status of the current user's chat session
		/// </summary>
		[HttpGet("status")]
		public async Task<IActionResult> GetSessionStatus(CancellationToken cancellationToken)
		{
			var sessionId = GetSessionId();
			var userId = GetUserId();
			var activeSessionsCount = _sessionManager.GetActiveSessionCount();
			var session = _sessionManager.GetSession(sessionId);
			var usage = await _usageQuotaService.GetStatusAsync(userId, cancellationToken);

			return Ok(new SessionStatusResponse
			{
				SessionId = sessionId,
                IsActive = session != null,
				ActiveSessionsCount = activeSessionsCount,
				CurrentMode = session?.CurrentMode.ToString(),
				   ContextLengthTokens = session?.CurrentContextLengthTokens,
				   MaxContextLengthTokens = session?.MaxContextLengthTokens ?? _defaultMaxContextLengthTokens,
				DailyQuotaLimit = usage.DailyLimit,
				DailyQuotaUsed = usage.Used,
				RemainingDailyQuota = usage.Remaining,
				QuotaResetAtUtc = usage.ResetAtUtc,
			   });
		}

		/// <summary>
		/// Health check endpoint for chat service
		/// </summary>
		[HttpGet("health")]
		[AllowAnonymous]
		public IActionResult HealthCheck()
		{
			return Ok(new
			{
				status = "healthy",
				activeSessions = _sessionManager.GetActiveSessionCount(),
				timestamp = DateTime.UtcNow
			});
		}

		/// <summary>
		/// Records token usage for the completed turn as a backstop; failures are logged, never surfaced.
		/// </summary>
		private async Task RecordTokenUsageAsync(string userId, long inputTokens, long outputTokens, CancellationToken cancellationToken)
		{
			try
			{
				await _usageQuotaService.RecordTokensAsync(userId, inputTokens, outputTokens, cancellationToken);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to record token usage for userId: {UserId}", userId);
			}
		}

		/// <summary>
		/// Gets the session ID from authentication claims
		/// </summary>
		private string GetSessionId()
		{
			var sessionId = User.FindFirstValue("SessionId");
			if (string.IsNullOrEmpty(sessionId))
			{
				throw new InvalidOperationException("Session ID not found in claims. User may need to re-login.");
			}
			return sessionId;
		}

		/// <summary>
		/// Gets the user ID from authentication claims
		/// </summary>
		private string GetUserId()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
			{
				throw new InvalidOperationException("User ID not found in claims");
			}
			return userId;
		}
	}
}
