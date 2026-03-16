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
	public async Task Delete_List_Cascades_To_TaskItems()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;

		var listRepo = new TaskListRepository(db);
		var taskRepo = new TaskItemRepository(db);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list);

		var task = new TaskItem("Task in list", list.Id);
		await taskRepo.AddAsync(task);

		var loadedTask = await taskRepo.GetAsync(task.Id);
		loadedTask!.TaskListId.Should().Be(list.Id);

		// Delete the list
		await listRepo.DeleteAsync(list);

		// Task should be deleted due to cascade delete
		var deletedTask = await taskRepo.GetAsync(task.Id);
		deletedTask.Should().BeNull();
	}

	[Fact]
	public async Task ContextId_IsPersistedAndLoaded()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new TaskListRepository(db);

		var contextId = Guid.NewGuid();
		var list = new TaskList("Work", contextId: contextId);
		await repo.AddAsync(list);

		var loaded = await repo.GetAsync(list.Id);

		loaded!.ContextId.Should().Be(contextId);
	}

	[Fact]
	public async Task List_FiltersByContextId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new TaskListRepository(db);

		var contextA = Guid.NewGuid();
		var contextB = Guid.NewGuid();
		var a1 = new TaskList("A1", contextId: contextA);
		var a2 = new TaskList("A2", contextId: contextA);
		var b1 = new TaskList("B1", contextId: contextB);
		await repo.AddAsync(a1);
		await repo.AddAsync(a2);
		await repo.AddAsync(b1);

		var forA = await repo.ListAsync(false, contextId: contextA);
		forA.Should().HaveCount(2);
		forA.Should().OnlyContain(x => x.ContextId == contextA);

		var forB = await repo.ListAsync(false, contextId: contextB);
		forB.Should().HaveCount(1);
		forB.Single().Id.Should().Be(b1.Id);
	}

	[Fact]
	public async Task List_ExcludesUnassigned_WhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new TaskListRepository(db);

		var contextId = Guid.NewGuid();
		var assigned1 = new TaskList("Assigned 1", contextId: contextId);
		var assigned2 = new TaskList("Assigned 2", contextId: contextId);
		var legacy = new TaskList("Legacy");
		await repo.AddAsync(assigned1);
		await repo.AddAsync(assigned2);
		await repo.AddAsync(legacy);

		var withoutUnassigned = await repo.ListAsync(false, excludeUnassigned: true);
		withoutUnassigned.Should().HaveCount(2);
		withoutUnassigned.Should().OnlyContain(x => x.ContextId != null);

		var withUnassigned = await repo.ListAsync(false, excludeUnassigned: false);
		withUnassigned.Should().HaveCount(3);
	}
}
