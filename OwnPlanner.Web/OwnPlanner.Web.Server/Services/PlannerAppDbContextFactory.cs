using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Creates planner data contexts for the current web execution scope.
/// It prefers an explicit planner session scope and only falls back to the authenticated HTTP user
/// when planner data is actually accessed.
/// </summary>
internal sealed class PlannerAppDbContextFactory(
	string userDbDirectory,
	IPlannerSessionContextAccessor sessionContextAccessor,
	IHttpContextAccessor httpContextAccessor) : IPlannerDbContextFactory
{
	public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
	{
		var userId = sessionContextAccessor.Current?.UserId ?? ResolveAuthenticatedUserId(httpContextAccessor.HttpContext);
		var dbPath = Path.Combine(userDbDirectory, $"ownplanner-user-{userId}.db");
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={dbPath}")
			.Options;
		return ValueTask.FromResult(new AppDbContext(options));
	}

	public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(userId))
		{
			throw new ArgumentException("User id is required to delete a planner database.", nameof(userId));
		}

		var dbPath = Path.Combine(userDbDirectory, $"ownplanner-user-{userId}.db");

		// Remove the database and any SQLite side-car files left by WAL mode.
		foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		return Task.CompletedTask;
	}

	private static string ResolveAuthenticatedUserId(HttpContext? httpContext)
	{
		var userId = httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
		{
			throw new UnauthorizedAccessException("Authenticated user id is required for planner data access.");
		}

		return userId;
	}
}

