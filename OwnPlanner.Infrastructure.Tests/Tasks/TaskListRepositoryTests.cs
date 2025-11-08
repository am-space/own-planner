using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public class TaskListRepositoryTests
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

		var repo = new TaskListRepository(db);
		var list = new TaskList("Shopping", "Grocery items", "#FF5733");
		await repo.AddAsync(list);

		var loaded = await repo.GetAsync(list.Id);
		loaded!.Title.Should().Be("Shopping");
		loaded.Description.Should().Be("Grocery items");
		loaded.Color.Should().Be("#FF5733");
		loaded.IsArchived.Should().BeFalse();

		loaded.SetTitle("Weekly Shopping");
		loaded.Archive();
		await repo.UpdateAsync(loaded);
		
		var updated = await repo.GetAsync(list.Id);
		updated!.Title.Should().Be("Weekly Shopping");
		updated.IsArchived.Should().BeTrue();

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(list.Id)).Should().BeNull();
	}

	[Fact]
	public async Task List_Filters_Archived_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new TaskListRepository(db);

		var personal = new TaskList("Personal");
		var work = new TaskList("Work");
		var archived = new TaskList("Old Projects");
		archived.Archive();

		await repo.AddAsync(personal);
		await repo.AddAsync(work);
		await repo.AddAsync(archived);

		var all = await repo.ListAsync(true);
		all.Should().HaveCount(3);

		var active = await repo.ListAsync(false);
		active.Should().HaveCount(2);
		active.Should().OnlyContain(x => !x.IsArchived);

		// UpdatedAt ordering desc
		personal.SetDescription("Personal tasks");
		await repo.UpdateAsync(personal);
		var ordered = await repo.ListAsync(true);
		ordered.First().Id.Should().Be(personal.Id);
	}

	[Fact]
	public async Task Delete_List_Sets_TaskListId_To_Null()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		
		var listRepo = new TaskListRepository(db);
		var taskRepo = new TaskItemRepository(db);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list);

		var task = new TaskItem("Task in list");
		task.AssignToList(list.Id);
		await taskRepo.AddAsync(task);

		var loadedTask = await taskRepo.GetAsync(task.Id);
		loadedTask!.TaskListId.Should().Be(list.Id);

		// Delete the list
		await listRepo.DeleteAsync(list);

		// Task should still exist but with null TaskListId
		var orphanedTask = await taskRepo.GetAsync(task.Id);
		orphanedTask.Should().NotBeNull();
		orphanedTask!.TaskListId.Should().BeNull();
	}
}
