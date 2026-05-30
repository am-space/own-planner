using System.Text.Json;

namespace OwnPlanner.Application.Chat;

/// <summary>
/// Transport-neutral description of an MCP-compatible tool.
/// This keeps tool discovery stable even when chat switches between stdio,
/// HTTP, or direct in-process execution.
/// </summary>
public sealed record McpToolDefinition(
	string Name,
	string Description,
	JsonElement? JsonSchema);

