using System.Security.Claims;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Helpers for deriving a planner <see cref="SessionContext"/> from the authenticated user on a
/// request, so the claim-to-context mapping lives in one place.
/// </summary>
public static class PlannerSessionContextExtensions
{
	/// <summary>
	/// Builds a planner <see cref="SessionContext"/> for the authenticated user. The user id is the
	/// raw <see cref="ClaimTypes.NameIdentifier"/> claim — the same value the per-user database path
	/// is resolved from elsewhere — and the <c>SessionId</c> claim is used when present, otherwise a
	/// stable <c>"{fallbackSessionPrefix}-{userId}"</c> is synthesized.
	/// </summary>
	/// <param name="user">The authenticated principal.</param>
	/// <param name="fallbackSessionPrefix">Prefix for the synthesized session id when no SessionId claim exists.</param>
	/// <exception cref="UnauthorizedAccessException">No authenticated user id claim is present.</exception>
	public static SessionContext GetRequiredPlannerSessionContext(this ClaimsPrincipal user, string fallbackSessionPrefix)
	{
		var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
		{
			throw new UnauthorizedAccessException("Authenticated user id is required for planner access.");
		}

		var sessionId = user.FindFirstValue("SessionId");
		return new SessionContext
		{
			SessionId = string.IsNullOrWhiteSpace(sessionId) ? $"{fallbackSessionPrefix}-{userId}" : sessionId,
			UserId = userId
		};
	}
}
