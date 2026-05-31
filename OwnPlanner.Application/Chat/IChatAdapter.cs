namespace OwnPlanner.Application.Chat;

/// <summary>
/// Abstraction over the conversational model used by planning workflows.
/// Implementations manage chat session state, expose context usage metadata, and coordinate
/// tool-enabled responses without leaking model-specific details into application orchestration.
/// </summary>
public interface IChatAdapter : IAsyncDisposable
{
	/// <summary>
	/// Gets the UTC timestamp when the chat adapter instance was created.
	/// This is used for session lifecycle tracking and cleanup decisions.
	/// </summary>
	DateTime CreatedTime { get; }

	/// <summary>
	/// Gets the UTC timestamp of the most recent chat interaction handled by this adapter.
	/// This reflects actual usage and supports inactivity-based session management.
	/// </summary>
	DateTime LastAccessTime { get; }

	/// <summary>
	/// Gets the current prompt-side context length, measured in tokens when available.
	/// Implementations can return <see langword="null"/> when the underlying model does not expose this metadata.
	/// </summary>
	int? CurrentContextLengthTokens { get; }

	/// <summary>
	/// Sends a user message to the conversational model and returns the assistant turn result.
	/// Implementations may perform tool calls as part of producing the final response.
	/// </summary>
	/// <param name="text">The user message to submit to the active chat session.</param>
	/// <returns>The resulting assistant turn, including response text and any available token metadata.</returns>
	Task<ChatTurnResult> GetResponse(string text);

	/// <summary>
	/// Resets the underlying chat session and optionally applies a new system prompt and tool allow-list.
	/// This is used when switching planning modes or recovering from corrupted conversation state.
	/// </summary>
	/// <param name="systemPrompt">Optional replacement system prompt for the new chat session.</param>
	/// <param name="allowedTools">Optional list of tool names that the new session is allowed to use.</param>
	void ResetChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null);
}
