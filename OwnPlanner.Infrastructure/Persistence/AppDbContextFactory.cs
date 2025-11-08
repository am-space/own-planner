using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OwnPlanner.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for creating AppDbContext instances for EF migrations
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
	public AppDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
		optionsBuilder.UseSqlite("Data Source=ownplanner.db");
		
		return new AppDbContext(optionsBuilder.Options);
	}
}
