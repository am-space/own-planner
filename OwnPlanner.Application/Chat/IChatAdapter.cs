namespace OwnPlanner.Application.Chat;

public interface IChatAdapter : IAsyncDisposable
{
	DateTime CreatedTime { get; }
	DateTime LastAccessTime { get; }
	Task<string> GetResponse(string text);
	void ResetChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null);
}
