namespace OwnPlanner.Web.Server.Authentication;

/// <summary>
/// Resolves MCP bearer token values to the owning planner user identifier.
/// </summary>
internal interface IMcpBearerTokenResolver
{
	/// <summary>
	/// Attempts to resolve a user identifier for the specified bearer token value.
	/// </summary>
	/// <param name="token">Raw bearer token value from the request header.</param>
	/// <param name="userId">Resolved planner user identifier when token is valid.</param>
	/// <returns><c>true</c> when the token is recognized; otherwise, <c>false</c>.</returns>
	bool TryResolveUserId(string token, out string userId);
}
