using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Web.Server.Authentication;

/// <summary>
/// Validates the user behind an authentication cookie on every request. Cookie auth is otherwise
/// stateless, so without this a cookie stays valid until it expires — meaning a deleted or
/// deactivated account could keep accessing the app from other sessions/devices. Rejecting the
/// principal here makes account deletion/deactivation take effect on the next request everywhere.
/// </summary>
public static class CookiePrincipalValidator
{
	/// <summary>
	/// Cookie <c>OnValidatePrincipal</c> hook: rejects and signs out the principal when its user no
	/// longer exists or is inactive.
	/// </summary>
	public static async Task ValidateAsync(CookieValidatePrincipalContext context)
	{
		var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();

		if (await IsPrincipalValidAsync(context.Principal, userRepository, context.HttpContext.RequestAborted))
		{
			return;
		}

		context.RejectPrincipal();
		await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
	}

	/// <summary>
	/// Returns whether the principal maps to an existing, active user.
	/// </summary>
	internal static async Task<bool> IsPrincipalValidAsync(
		ClaimsPrincipal? principal,
		IUserRepository userRepository,
		CancellationToken cancellationToken)
	{
		var userIdClaim = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
		if (!Guid.TryParse(userIdClaim, out var userId))
		{
			return false;
		}

		var user = await userRepository.GetByIdAsync(userId, cancellationToken);
		return user is not null && user.IsActive;
	}
}
