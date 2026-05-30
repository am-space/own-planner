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
}

