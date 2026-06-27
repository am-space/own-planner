using System.Threading;
using System.Threading.Tasks;

namespace OwnPlanner.Infrastructure.Persistence;

/// <summary>
/// Creates planner <see cref="AppDbContext"/> instances for the current execution scope.
/// Implementations are responsible for resolving the correct per-user database and callers
/// must dispose each returned context after use.
/// </summary>
public interface IPlannerDbContextFactory
{
	/// <summary>
	/// Creates a planner data context for the current execution scope.
	/// </summary>
	/// <param name="cancellationToken">Signals that the caller has lost interest before context creation completes.</param>
	/// <returns>A new planner data context bound to the current execution scope.</returns>
	ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Permanently deletes the planner database belonging to the specified user, including any SQLite
	/// side-car files (WAL/SHM). Used when erasing an account. Implementations should be best-effort:
	/// a missing database is treated as already deleted.
	/// </summary>
	/// <param name="userId">The identifier of the user whose planner database should be removed.</param>
	/// <param name="cancellationToken">Signals that the caller has lost interest before deletion completes.</param>
	Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default);
}

