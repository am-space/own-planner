using OwnPlanner.Application.Chat;

namespace OwnPlanner.E2E.Tests.Infrastructure;

internal sealed class ScriptedChatAdapter(
	ScriptedChatScenarioRegistry scenarios,
	IMcpAdapter? mcpAdapter) : IChatAdapter
{
	public DateTime CreatedTime { get; } = DateTime.UtcNow;
	public DateTime LastAccessTime { get; private set; } = DateTime.UtcNow;
	public int? CurrentContextLengthTokens { get; private set; }

	public async Task<ChatTurnResult> GetResponse(string text, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		LastAccessTime = DateTime.UtcNow;
		var result = await scenarios.ExecuteAsync(text, mcpAdapter).ConfigureAwait(false);
		CurrentContextLengthTokens = result.ContextLengthTokens;
		return result;
	}

	public Task<ChatTurnResult> GetResponse(string text) => GetResponse(text, CancellationToken.None);

	public void ResetChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null)
	{
		CurrentContextLengthTokens = null;
	}

	public void RebuildSession(string systemPrompt, IReadOnlyList<string>? allowedTools, IReadOnlyList<ChatMessage> history)
	{
		CurrentContextLengthTokens = null;
	}

	public Task<string> SummarizeAsync(string conversationText, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult("Scripted E2E conversation summary.");
	}

	public async ValueTask DisposeAsync()
	{
		if (mcpAdapter is not null)
		{
			await mcpAdapter.DisposeAsync().ConfigureAwait(false);
		}
	}
}
