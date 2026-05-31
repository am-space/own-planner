namespace OwnPlanner.Mcp.Tools;

/// <summary>
/// Context information for the current MCP session, used by tools to identify
/// the originating user and session for logging and diagnostics.
/// </summary>
public class SessionContext
{
	public required string SessionId { get; init; }
	public required string UserId { get; init; }
}

