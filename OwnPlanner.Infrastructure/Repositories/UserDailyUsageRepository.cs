using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

/// <summary>
/// Repository for per-user, per-day usage tracking. Uses SQLite "upsert"
/// (<c>INSERT ... ON CONFLICT DO UPDATE</c>) so the create-or-increment is a single atomic statement —
/// concurrent requests from the same user cannot lose increments.
/// </summary>
public class UserDailyUsageRepository(AuthDbContext context)
	: RepositoryBase<UserDailyUsage, AuthDbContext>(context), IUserDailyUsageRepository
{
	public async Task<int> IncrementRequestAsync(Guid userId, DateOnly dateUtc, CancellationToken cancellationToken = default)
	{
		// Pass Guid/DateOnly as native parameters so EF's SQLite type mappings serialize them exactly the way
		// the stored column values were written (e.g. Guid as upper-case TEXT) — the conflict target and the
		// read-back below then compare against identical strings.
		var id = Guid.NewGuid();
		var now = DateTime.UtcNow;

		await Db.Database.ExecuteSqlInterpolatedAsync(
			$"""
			INSERT INTO "UserDailyUsages" ("Id", "UserId", "Date", "RequestCount", "InputTokens", "OutputTokens", "CreatedAt", "UpdatedAt")
			VALUES ({id}, {userId}, {dateUtc}, 1, 0, 0, {now}, {now})
			ON CONFLICT ("UserId", "Date") DO UPDATE SET "RequestCount" = "RequestCount" + 1, "UpdatedAt" = {now}
			""",
			cancellationToken);

		// Request counts only ever increase within a day, so reading back after the atomic increment can
		// never return a value below our own contribution; a concurrently-higher value only enforces harder.
		return await Set
			.Where(u => u.UserId == userId && u.Date == dateUtc)
			.Select(u => u.RequestCount)
			.FirstAsync(cancellationToken);
	}

	public async Task AddTokensAsync(Guid userId, DateOnly dateUtc, long inputTokens, long outputTokens, CancellationToken cancellationToken = default)
	{
		// Upsert rather than a plain UPDATE: the day row normally already exists (created by the request
		// reservation), but token accounting must also work when enforcement is disabled — in which case no
		// reservation happened and there is no row yet. ON CONFLICT keeps it atomic either way.
		var id = Guid.NewGuid();
		var now = DateTime.UtcNow;
		await Db.Database.ExecuteSqlInterpolatedAsync(
			$"""
			INSERT INTO "UserDailyUsages" ("Id", "UserId", "Date", "RequestCount", "InputTokens", "OutputTokens", "CreatedAt", "UpdatedAt")
			VALUES ({id}, {userId}, {dateUtc}, 0, {inputTokens}, {outputTokens}, {now}, {now})
			ON CONFLICT ("UserId", "Date") DO UPDATE SET "InputTokens" = "InputTokens" + {inputTokens},
			    "OutputTokens" = "OutputTokens" + {outputTokens},
			    "UpdatedAt" = {now}
			""",
			cancellationToken);
	}

	public async Task<UserDailyUsage?> GetAsync(Guid userId, DateOnly dateUtc, CancellationToken cancellationToken = default)
		=> await Set.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId && u.Date == dateUtc, cancellationToken);
}
