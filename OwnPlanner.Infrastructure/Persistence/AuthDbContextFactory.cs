using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OwnPlanner.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for AuthDbContext to support EF Core migrations.
/// Usage: dotnet ef migrations add MigrationName --context AuthDbContext
/// </summary>
public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
	public AuthDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();
		optionsBuilder.UseSqlite("Data Source=auth.db");
		return new AuthDbContext(optionsBuilder.Options);
	}
}
