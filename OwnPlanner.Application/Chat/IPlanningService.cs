namespace OwnPlanner.Application.Chat;

public interface IPlanningService : IAsyncDisposable
{
	DateTime CreatedTime { get; }
	DateTime LastAccessTime { get; }
   int? CurrentContextLengthTokens { get; }
	PlanningMode CurrentMode { get; }
	Task SwitchModeAsync(PlanningMode mode, CancellationToken cancellationToken = default);
   Task<ChatTurnResult> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default);
}
