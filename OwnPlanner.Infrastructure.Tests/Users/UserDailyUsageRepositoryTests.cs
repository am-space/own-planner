using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Users;

public class UserDailyUsageRepositoryTests
{
	private static readonly DateOnly Today = new(2026, 6, 14);

	private static AuthDbContext CreateDb(SqliteConnection conn)
	{
		var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(conn).Options;
		return new AuthDbContext(options);
	}

	private static async Task<Guid> SeedUserAsync(AuthDbContext db, CancellationToken ct)
	{
		var user = new User($"{Guid.NewGuid():N}@example.com", "user", "hash");
		db.Users.Add(user);
		await db.SaveChangesAsync(ct);
		return user.Id;
	}

	private static async Task<string> ReadScalarTextAsync(SqliteConnection conn, string sql, CancellationToken ct)
	{
		await using var cmd = conn.CreateCommand();
		cmd.CommandText = sql;
		var value = await cmd.ExecuteScalarAsync(ct);
		return (string)value!;
	}

	[Fact]
	public async Task IncrementRequest_CreatesRow_ThenIncrements_ReturningNewCount()
	{
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);

		(await repo.IncrementRequestAsync(userId, Today, ct)).Should().Be(1);
		(await repo.IncrementRequestAsync(userId, Today, ct)).Should().Be(2);
		(await repo.IncrementRequestAsync(userId, Today, ct)).Should().Be(3);

		var row = await repo.GetAsync(userId, Today, ct);
		row!.RequestCount.Should().Be(3);
	}

	[Fact]
	public async Task IncrementRequest_StoresUserIdInSameTextFormatEfUses()
	{
		// Regression guard for the raw-SQL upsert: it must serialize the Guid to the exact TEXT format EF
		// Core's SQLite provider used when writing Users.Id (upper-case), otherwise the foreign key and the
		// ON CONFLICT ("UserId","Date") target silently fail to match. Passing the native Guid parameter (not
		// userId.ToString(), which is lower-case) is what keeps the two strings identical.
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);

		await repo.IncrementRequestAsync(userId, Today, ct);

		// Read the raw stored strings with ADO (no EF type conversion) and assert they are byte-identical.
		var usersIdText = await ReadScalarTextAsync(conn, "SELECT \"Id\" FROM \"Users\" LIMIT 1", ct);
		var usageUserIdText = await ReadScalarTextAsync(conn, "SELECT \"UserId\" FROM \"UserDailyUsages\" LIMIT 1", ct);

		usageUserIdText.Should().Be(usersIdText);
	}

	[Fact]
	public async Task AddTokens_TargetsTheRowKeyedByTheSameGuidFormat()
	{
		// AddTokens locates the row with WHERE "UserId" = {userId}; this fails to match if the upsert wrote
		// the id in a different TEXT format than this parameter serializes to. Asserting the tokens actually
		// land proves the WHERE matched the upsert-created row.
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);

		await repo.IncrementRequestAsync(userId, Today, ct);
		await repo.AddTokensAsync(userId, Today, 700, 90, ct);

		var row = await repo.GetAsync(userId, Today, ct);
		row!.InputTokens.Should().Be(700);
		row.OutputTokens.Should().Be(90);
	}

	[Fact]
	public async Task AddTokens_AccumulatesOntoExistingRow()
	{
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);

		await repo.IncrementRequestAsync(userId, Today, ct);
		await repo.AddTokensAsync(userId, Today, 1000, 200, ct);
		await repo.AddTokensAsync(userId, Today, 500, 50, ct);

		var row = await repo.GetAsync(userId, Today, ct);
		row!.InputTokens.Should().Be(1500);
		row.OutputTokens.Should().Be(250);
	}

	[Fact]
	public async Task AddTokens_CreatesRow_WhenNoneExists()
	{
		// When enforcement is disabled no request reservation runs, so there is no day row yet. Token
		// accounting must still persist (the backstop calibrates cost even while limits are off), so
		// AddTokens upserts rather than no-opping on a missing row.
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);

		await repo.AddTokensAsync(userId, Today, 800, 120, ct);

		var row = await repo.GetAsync(userId, Today, ct);
		row.Should().NotBeNull();
		row.RequestCount.Should().Be(0);
		row.InputTokens.Should().Be(800);
		row.OutputTokens.Should().Be(120);
	}

	[Fact]
	public async Task IncrementRequest_IsIsolatedPerDay()
	{
		var conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		using var db = CreateDb(conn);
		await db.Database.EnsureCreatedAsync(ct);
		var userId = await SeedUserAsync(db, ct);
		var repo = new UserDailyUsageRepository(db);
		var tomorrow = Today.AddDays(1);

		await repo.IncrementRequestAsync(userId, Today, ct);
		await repo.IncrementRequestAsync(userId, Today, ct);
		await repo.IncrementRequestAsync(userId, tomorrow, ct);

		(await repo.GetAsync(userId, Today, ct))!.RequestCount.Should().Be(2);
		(await repo.GetAsync(userId, tomorrow, ct))!.RequestCount.Should().Be(1);
	}

	[Fact]
	public async Task IncrementRequest_ConcurrentCalls_DoNotLoseUpdates()
	{
		// A shared file-backed database so each task can use its own connection/context and contend for the
		// write lock, proving the atomic upsert never loses a concurrent increment.
		var path = Path.Combine(Path.GetTempPath(), $"usage-{Guid.NewGuid():N}.db");
		var connectionString = $"Data Source={path}";
		var ct = TestContext.Current.CancellationToken;
		const int concurrentRequests = 25;

		try
		{
			Guid userId;
			await using (var seedConn = new SqliteConnection(connectionString))
			{
				await seedConn.OpenAsync(ct);
				using var seedDb = CreateDb(seedConn);
				await seedDb.Database.EnsureCreatedAsync(ct);
				userId = await SeedUserAsync(seedDb, ct);
			}

			var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
			{
				await using var conn = new SqliteConnection(connectionString);
				await conn.OpenAsync(ct);
				using var db = CreateDb(conn);
				var repo = new UserDailyUsageRepository(db);
				return await repo.IncrementRequestAsync(userId, Today, ct);
			});

			var results = await Task.WhenAll(tasks);

			// Every increment returned a distinct value and the final stored count equals the number of requests.
			results.Should().OnlyHaveUniqueItems();
			results.Max().Should().Be(concurrentRequests);

			await using var verifyConn = new SqliteConnection(connectionString);
			await verifyConn.OpenAsync(ct);
			using var verifyDb = CreateDb(verifyConn);
			var row = await new UserDailyUsageRepository(verifyDb).GetAsync(userId, Today, ct);
			row!.RequestCount.Should().Be(concurrentRequests);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}
}
