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
		var listRepo = new TaskListRepository(db);
		
		var list = new TaskList("Test List");
		await listRepo.AddAsync(list);
		
		var item = new TaskItem("test", list.Id);
		await repo.AddAsync(item);

		var loaded = await repo.GetAsync(item.Id);
		loaded!.Title.Should().Be("test");
		loaded.TaskListId.Should().Be(list.Id);

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
		var listRepo = new TaskListRepository(db);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list);

		var a = new TaskItem("a", list.Id);
		var b = new TaskItem("b", list.Id);
		var c = new TaskItem("c", list.Id);
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

	[Fact]
	public async Task ListByTaskList_Filters_By_TaskListId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var taskRepo = new TaskItemRepository(db);
		var listRepo = new TaskListRepository(db);

		var list1 = new TaskList("List 1");
		var list2 = new TaskList("List 2");
		await listRepo.AddAsync(list1);
		await listRepo.AddAsync(list2);

		var task1 = new TaskItem("Task in List 1", list1.Id);
		var task2 = new TaskItem("Task in List 2", list2.Id);
		var task3 = new TaskItem("Another task in List 1", list1.Id);
		task3.Complete();

		await taskRepo.AddAsync(task1);
		await taskRepo.AddAsync(task2);
		await taskRepo.AddAsync(task3);

		// Get tasks for list1 including completed
		var list1Tasks = await taskRepo.ListByTaskListAsync(list1.Id, true);
		list1Tasks.Should().HaveCount(2);
		list1Tasks.Should().OnlyContain(t => t.TaskListId == list1.Id);

		// Get tasks for list1 excluding completed
		var list1ActiveTasks = await taskRepo.ListByTaskListAsync(list1.Id, false);
		list1ActiveTasks.Should().HaveCount(1);
		list1ActiveTasks.First().Id.Should().Be(task1.Id);

		// Get tasks for list2
		var list2Tasks = await taskRepo.ListByTaskListAsync(list2.Id, true);
		list2Tasks.Should().HaveCount(1);
		list2Tasks.First().Id.Should().Be(task2.Id);
	}
}
