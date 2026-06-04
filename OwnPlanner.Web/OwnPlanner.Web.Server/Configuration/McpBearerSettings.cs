namespace OwnPlanner.Web.Server.Configuration;

/// <summary>
/// Configuration for MCP HTTP bearer token authentication.
/// </summary>
public sealed class McpBearerSettings
{
	public const string SectionName = "McpBearer";

	public List<McpBearerTokenBinding> TokenBindings { get; init; } = [];
}

/// <summary>
/// Maps a bearer token value to a planner user ID.
/// </summary>
public sealed class McpBearerTokenBinding
{
	public string Token { get; init; } = string.Empty;
	public string UserId { get; init; } = string.Empty;
}
