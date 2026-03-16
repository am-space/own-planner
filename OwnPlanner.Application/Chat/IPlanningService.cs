namespace OwnPlanner.Application.Chat;

public interface IPlanningService : IAsyncDisposable
{
	DateTime CreatedTime { get; }
	DateTime LastAccessTime { get; }
	PlanningMode CurrentMode { get; }
	Task SwitchModeAsync(PlanningMode mode, CancellationToken cancellationToken = default);
	Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default);
}
