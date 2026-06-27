using System.IO.Compression;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Account;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Tests.Account;

public class AccountExportServiceTests
{
	private static AppDbContext CreateDb(out SqliteConnection conn)
	{
		conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
		var db = new AppDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	[Fact]
	public async Task CreateExportAsync_ProducesZipWithDatabaseAndReadme()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;

		var list = new TaskList("Shopping", "Grocery items", "#FF5733");
		db.TaskLists.Add(list);
		db.TaskItems.Add(new TaskItem("Buy milk", list.Id));
		await db.SaveChangesAsync(ct);

		var service = new AccountExportService(new TestPlannerDbContextFactory(conn));

		var export = await service.CreateExportAsync(ct);

		try
		{
			export.ContentType.Should().Be("application/zip");
			export.FileName.Should().StartWith("ownplanner-export-").And.EndWith(".zip");
			File.Exists(export.FilePath).Should().BeTrue();

			using var archive = ZipFile.OpenRead(export.FilePath);
			archive.Entries.Select(e => e.FullName)
				.Should().BeEquivalentTo(AccountExportService.DatabaseEntryName, AccountExportService.ReadmeEntryName);
		}
		finally
		{
			if (File.Exists(export.FilePath))
			{
				File.Delete(export.FilePath);
			}
		}
	}

	[Fact]
	public async Task CreateExportAsync_SnapshotContainsSeededRows()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;

		var list = new TaskList("Work", null, null);
		db.TaskLists.Add(list);
		db.TaskItems.Add(new TaskItem("Write report", list.Id));
		await db.SaveChangesAsync(ct);

		var service = new AccountExportService(new TestPlannerDbContextFactory(conn));
		var export = await service.CreateExportAsync(ct);

		var extractedDbPath = Path.Combine(Path.GetTempPath(), $"ownplanner-export-test-{Guid.NewGuid():N}.db");
		try
		{
			using (var archive = ZipFile.OpenRead(export.FilePath))
			{
				var entry = archive.GetEntry(AccountExportService.DatabaseEntryName);
				entry.Should().NotBeNull();
				entry!.ExtractToFile(extractedDbPath, overwrite: true);
			}

			// Open the snapshot as an independent SQLite database and verify the data round-trips.
			await using var snapshotConn = new SqliteConnection($"DataSource={extractedDbPath}");
			await snapshotConn.OpenAsync(ct);
			await using var command = snapshotConn.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM TaskItems WHERE Title = 'Write report'";
			var count = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
			count.Should().Be(1);
		}
		finally
		{
			if (File.Exists(export.FilePath))
			{
				File.Delete(export.FilePath);
			}

			if (File.Exists(extractedDbPath))
			{
				File.Delete(extractedDbPath);
			}
		}
	}
}
