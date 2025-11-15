using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
		private readonly ChatSessionManager _sessionManager;
		private readonly ILogger<ChatController> _logger;

		public ChatController(ChatSessionManager sessionManager, ILogger<ChatController> logger)
		{
			_sessionManager = sessionManager;
			_logger = logger;
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

			try
			{
				var chatService = await _sessionManager.GetOrCreateSessionAsync(sessionId, userId, cancellationToken);
				var response = await chatService.GetResponse(request.Message);

				_logger.LogInformation("Chat response generated for sessionId: {SessionId}", sessionId);

				return Ok(new ChatResponse
				{
					Message = response,
					SessionId = sessionId,
					Timestamp = DateTime.UtcNow
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing chat message for sessionId: {SessionId}", sessionId);
				return StatusCode(500, new { message = "An error occurred while processing your message" });
			}
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
		public IActionResult GetSessionStatus()
		{
			var sessionId = GetSessionId();
			var activeSessionsCount = _sessionManager.GetActiveSessionCount();

			return Ok(new SessionStatusResponse
			{
				SessionId = sessionId,
				IsActive = true,
				ActiveSessionsCount = activeSessionsCount
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
