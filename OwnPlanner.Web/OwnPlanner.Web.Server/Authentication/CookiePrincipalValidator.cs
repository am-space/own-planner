using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Web.Server.Authentication;

/// <summary>
/// Validates the user behind an authentication cookie. Cookie auth is otherwise stateless, so without
/// this a cookie stays valid until it expires — meaning a deleted or deactivated account could keep
/// accessing the app from other sessions/devices. Rejecting the principal here makes account
/// deletion/deactivation take effect everywhere.
/// <para>
/// To avoid a database read on every request, a validated cookie is trusted for
/// <see cref="ValidationInterval"/> (the timestamp is stamped into the cookie). This bounds the work
/// to roughly one lookup per user per interval and limits exposure to transient database errors; the
/// cost is that revocation can lag by up to that interval.
/// </para>
/// </summary>
public static class CookiePrincipalValidator
{
	/// <summary>How long a validated cookie is trusted before its user is re-checked against the DB.</summary>
	internal static readonly TimeSpan ValidationInterval = TimeSpan.FromMinutes(15);

	internal const string LastValidatedTicksKey = "UserValidatedUtcTicks";

	/// <summary>
	/// Cookie <c>OnValidatePrincipal</c> hook: rejects and signs out the principal when its user no
	/// longer exists or is inactive, throttled by <see cref="ValidationInterval"/>.
	/// </summary>
	public static async Task ValidateAsync(CookieValidatePrincipalContext context)
	{
		var now = DateTimeOffset.UtcNow;

		// Trust a recently validated cookie rather than hitting the DB on every request.
		if (!IsRevalidationDue(context.Properties, now))
		{
			return;
		}

		bool valid;
		try
		{
			var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
			valid = await IsPrincipalValidAsync(context.Principal, userRepository, context.HttpContext.RequestAborted);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Fail open on a transient lookup failure so a DB blip doesn't mass-log-out active users.
			// Don't stamp the time, so the next request re-checks as soon as the DB recovers.
			context.HttpContext.RequestServices
				.GetRequiredService<ILoggerFactory>()
				.CreateLogger(typeof(CookiePrincipalValidator).FullName!)
				.LogWarning(ex, "Cookie principal validation lookup failed; allowing the request and retrying next time");
			return;
		}

		if (!valid)
		{
			context.RejectPrincipal();
			await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return;
		}

		// Record the successful validation and renew the cookie so the timestamp persists.
		StampValidated(context.Properties, now);
		context.ShouldRenew = true;
	}

	/// <summary>Whether the cookie is due for a fresh database check based on its last-validated stamp.</summary>
	internal static bool IsRevalidationDue(AuthenticationProperties? properties, DateTimeOffset utcNow)
	{
		var raw = properties?.GetString(LastValidatedTicksKey);
		if (raw is null || !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
		{
			return true;
		}

		var lastValidated = new DateTimeOffset(ticks, TimeSpan.Zero);
		return utcNow - lastValidated >= ValidationInterval;
	}

	/// <summary>Records the time of a successful validation in the cookie properties.</summary>
	internal static void StampValidated(AuthenticationProperties? properties, DateTimeOffset utcNow)
	{
		properties?.SetString(LastValidatedTicksKey, utcNow.UtcTicks.ToString(CultureInfo.InvariantCulture));
	}

	/// <summary>Returns whether the principal maps to an existing, active user.</summary>
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
