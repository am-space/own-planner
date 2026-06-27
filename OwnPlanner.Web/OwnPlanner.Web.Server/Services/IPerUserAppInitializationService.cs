using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Ensures a user's planner database is migrated and seeded before it is accessed. Implementations
/// are expected to be idempotent and to de-duplicate concurrent initialization for the same user.
/// </summary>
public interface IPerUserAppInitializationService
{
	/// <summary>
	/// Ensures the planner database for the user described by <paramref name="sessionContext"/> has
	/// been migrated and seeded, performing the work once per user even under concurrent callers.
	/// </summary>
	Task EnsureInitializedAsync(SessionContext sessionContext, CancellationToken cancellationToken = default);
}
