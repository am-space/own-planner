using System.Security.Claims;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Ensures per-user planner database initialization before handling authenticated MCP HTTP requests.
/// </summary>
public sealed class McpRequestInitializationMiddleware(PerUserAppInitializationService initializationService)
	: IMiddleware
{
	public async Task InvokeAsync(HttpContext context, RequestDelegate next)
	{
		if (context.User.Identity?.IsAuthenticated == true)
		{
			var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
			{
				throw new UnauthorizedAccessException("Authenticated user id is required for MCP access.");
			}

			var sessionId = context.User.FindFirstValue("SessionId");
			await initializationService.EnsureInitializedAsync(
				new SessionContext
				{
					SessionId = string.IsNullOrWhiteSpace(sessionId) ? $"mcp-{userId}" : sessionId,
					UserId = userId
				},
				context.RequestAborted).ConfigureAwait(false);
		}

		await next(context).ConfigureAwait(false);
	}
}
