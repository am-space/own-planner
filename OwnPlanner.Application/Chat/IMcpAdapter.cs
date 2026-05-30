namespace OwnPlanner.Application.Chat;

/// <summary>
/// Abstraction over planner tool execution for chat workflows.
/// This interface keeps the Application layer independent from any specific MCP transport
/// or hosting model, so chat orchestration can work with stdio, HTTP, or direct in-process
/// tool execution without changing planning behavior.
/// </summary>
public interface IMcpAdapter : IAsyncDisposable
{
	/// <summary>
	/// Prepares the underlying tool adapter for use.
	/// Implementations can use this hook to establish remote connections, warm up state,
	/// or validate that tool execution is available before the first call.
	/// </summary>
	/// <param name="cancellationToken">Cancels the initialization work.</param>
	Task InitializeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the tool metadata that the chat adapter uses to expose tool capabilities to the model.
	/// The returned definitions are transport-neutral so the Application layer depends only on the
	/// information needed for orchestration: tool name, description, and input schema.
	/// </summary>
	/// <param name="cancellationToken">Cancels the metadata retrieval.</param>
	/// <returns>A read-only collection describing the available tools.</returns>
	Task<IReadOnlyList<McpToolDefinition>> ListToolDetailsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Executes a tool by name and returns the serialized result payload that should be sent back to the model.
	/// Implementations are responsible for binding the provided arguments to the underlying tool system and
	/// normalizing the response into a string form that chat orchestration can safely forward.
	/// </summary>
	/// <param name="toolName">The MCP tool name to invoke.</param>
	/// <param name="arguments">Optional tool arguments keyed by parameter name.</param>
	/// <param name="cancellationToken">Cancels the tool invocation.</param>
	/// <returns>The serialized tool result payload.</returns>
	Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default);
}
