namespace OwnPlanner.Application.Chat;

public interface IMcpAdapter : IAsyncDisposable
{
	Task InitializeAsync(CancellationToken cancellationToken = default);
	Task<List<string>> ListToolNamesAsync(CancellationToken cancellationToken = default);
	Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default);
}
