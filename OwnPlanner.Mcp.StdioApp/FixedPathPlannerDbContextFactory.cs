using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Mcp.StdioApp;

/// <summary>
/// Creates planner data contexts for the stdio host using a fixed database path derived at startup.
/// </summary>
internal sealed class FixedPathPlannerDbContextFactory(string dbPath) : IPlannerDbContextFactory
{
	private readonly string _dbPath = dbPath;

	public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
	{
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={_dbPath}")
			.Options;
		return ValueTask.FromResult(new AppDbContext(options));
	}
}

