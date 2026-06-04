using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Authentication;

internal sealed class McpBearerAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IMcpBearerTokenResolver tokenResolver)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader) ||
			string.IsNullOrWhiteSpace(authorizationHeader))
		{
			return Task.FromResult(AuthenticateResult.NoResult());
		}

		if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var parsedHeader) ||
			!string.Equals(parsedHeader.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
		{
			return Task.FromResult(AuthenticateResult.NoResult());
		}

		var token = parsedHeader.Parameter;
		if (string.IsNullOrWhiteSpace(token))
		{
			return Task.FromResult(AuthenticateResult.Fail("Missing bearer token."));
		}

		if (!tokenResolver.TryResolveUserId(token, out var userId))
		{
			return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
		}

		var sessionContext = new SessionContext
		{
			SessionId = $"mcp-{userId}",
			UserId = userId
		};

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, sessionContext.UserId),
			new(ClaimTypes.Name, sessionContext.UserId),
			new("SessionId", sessionContext.SessionId)
		};
		var identity = new ClaimsIdentity(claims, McpBearerAuthenticationDefaults.AuthenticationScheme);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, McpBearerAuthenticationDefaults.AuthenticationScheme);
		return Task.FromResult(AuthenticateResult.Success(ticket));
	}
}
