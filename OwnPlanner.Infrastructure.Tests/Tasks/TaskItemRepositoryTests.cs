using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public class TaskItemRepositoryTests
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
	public async Task Add_Get_Update_Delete_Roundtrip()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;

		var repo = new TaskItemRepository(db);
		var item = new TaskItem("test");
		await repo.AddAsync(item);

		var loaded = await repo.GetAsync(item.Id);
		loaded!.Title.Should().Be("test");

		loaded.Complete();
		await repo.UpdateAsync(loaded);
		(await repo.GetAsync(item.Id))!.IsCompleted.Should().BeTrue();

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(item.Id)).Should().BeNull();
	}

	[Fact]
	public async Task List_Filters_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new TaskItemRepository(db);

		var a = new TaskItem("a");
		var b = new TaskItem("b");
		var c = new TaskItem("c");
		b.Complete();
		await repo.AddAsync(a);
		await repo.AddAsync(b);
		await repo.AddAsync(c);

		var all = await repo.ListAsync(true);
		all.Should().HaveCount(3);

		var active = await repo.ListAsync(false);
		active.Should().OnlyContain(x => !x.IsCompleted);

		// UpdatedAt ordering desc
		a.SetDescription("zzz");
		await repo.UpdateAsync(a);
		var ordered = await repo.ListAsync(true);
		ordered.First().Id.Should().Be(a.Id);
	}
}
