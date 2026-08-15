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

	/// <summary>Sends a user message while propagating cancellation through model and tool orchestration.</summary>
	/// <param name="text">The user message to submit to the active chat session.</param>
	/// <param name="cancellationToken">Cancels model and tool orchestration for the turn.</param>
	Task<ChatTurnResult> GetResponse(string text, CancellationToken cancellationToken) => GetResponse(text);

	/// <summary>
	/// Resets the underlying chat session and optionally applies a new system prompt and tool allow-list.
	/// This is used when switching planning modes or recovering from corrupted conversation state.
	/// </summary>
	/// <param name="systemPrompt">Optional replacement system prompt for the new chat session.</param>
	/// <param name="allowedTools">Optional list of tool names that the new session is allowed to use.</param>
	void ResetChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null);

	/// <summary>
	/// Rebuilds the chat session from a provided conversation history, preserving the system prompt and
	/// tool allow-list. Used by history compaction to replace a long transcript with a compacted one
	/// (summary + recent turns) without leaking the underlying chat SDK's history representation.
	/// </summary>
	/// <param name="systemPrompt">The system prompt to seed the rebuilt session with.</param>
	/// <param name="allowedTools">Optional list of tool names that the rebuilt session is allowed to use.</param>
	/// <param name="history">The conversation turns to replay into the rebuilt session, in order.</param>
	void RebuildSession(string systemPrompt, IReadOnlyList<string>? allowedTools, IReadOnlyList<ChatMessage> history);

	/// <summary>
	/// Produces a concise factual summary of earlier conversation text using a side session that does
	/// not affect the active chat. Used by history compaction.
	/// </summary>
	/// <param name="conversationText">The rendered earlier conversation to summarize.</param>
	/// <param name="cancellationToken">Cancels the summarization request.</param>
	/// <returns>A compact summary, or an empty string when there is nothing to summarize.</returns>
	Task<string> SummarizeAsync(string conversationText, CancellationToken cancellationToken = default);
}
