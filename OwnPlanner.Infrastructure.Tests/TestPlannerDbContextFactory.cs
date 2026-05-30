using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Tests;

internal sealed class TestPlannerDbContextFactory(SqliteConnection connection) : IPlannerDbContextFactory
{
	private readonly SqliteConnection _connection = connection;

	public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
	{
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite(_connection)
			.Options;
		return ValueTask.FromResult(new AppDbContext(options));
	}
}

