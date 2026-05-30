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
	private readonly string _userDbDirectory = userDbDirectory;
	private readonly IPlannerSessionContextAccessor _sessionContextAccessor = sessionContextAccessor;
	private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

	public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default)
	{
		var userId = _sessionContextAccessor.Current?.UserId ?? ResolveAuthenticatedUserId(_httpContextAccessor.HttpContext);
		var dbPath = Path.Combine(_userDbDirectory, $"ownplanner-user-{userId}.db");
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite($"Data Source={dbPath}")
			.Options;
		return ValueTask.FromResult(new AppDbContext(options));
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

