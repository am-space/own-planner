using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tests;

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
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);
		
		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);
		
		var item = new TaskItem("test", list.Id);
		await repo.AddAsync(item, ct);

		var loaded = await repo.GetAsync(item.Id, ct);
		loaded!.Title.Should().Be("test");
		loaded.TaskListId.Should().Be(list.Id);

		loaded.Complete();
		await repo.UpdateAsync(loaded, ct);
		(await repo.GetAsync(item.Id, ct))!.IsCompleted.Should().BeTrue();

		await repo.DeleteAsync(loaded, ct);
		(await repo.GetAsync(item.Id, ct)).Should().BeNull();
	}

	[Fact]
	public async Task List_Filters_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var a = new TaskItem("a", list.Id);
		var b = new TaskItem("b", list.Id);
		var c = new TaskItem("c", list.Id);
		b.Complete();
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);
		await repo.AddAsync(c, ct);

		var all = await repo.ListAsync(true, ct);
		all.Should().HaveCount(3);

		var active = await repo.ListAsync(false, ct);
		active.Should().OnlyContain(x => !x.IsCompleted);

		// UpdatedAt ordering desc
		a.SetDescription("zzz");
		await repo.UpdateAsync(a, ct);
		var ordered = await repo.ListAsync(true, ct);
		ordered.First().Id.Should().Be(a.Id);
	}

	[Fact]
	public async Task ListByTaskList_Filters_By_TaskListId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var taskRepo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list1 = new TaskList("List 1");
		var list2 = new TaskList("List 2");
		await listRepo.AddAsync(list1, ct);
		await listRepo.AddAsync(list2, ct);

		var task1 = new TaskItem("Task in List 1", list1.Id);
		var task2 = new TaskItem("Task in List 2", list2.Id);
		var task3 = new TaskItem("Another task in List 1", list1.Id);
		task3.Complete();

		await taskRepo.AddAsync(task1, ct);
		await taskRepo.AddAsync(task2, ct);
		await taskRepo.AddAsync(task3, ct);

		// Get tasks for list1 including completed
		var list1Tasks = await taskRepo.ListByTaskListAsync(list1.Id, true, ct);
		list1Tasks.Should().HaveCount(2);
		list1Tasks.Should().OnlyContain(t => t.TaskListId == list1.Id);

		// Get tasks for list1 excluding completed
		var list1ActiveTasks = await taskRepo.ListByTaskListAsync(list1.Id, false, ct);
		list1ActiveTasks.Should().HaveCount(1);
		list1ActiveTasks.First().Id.Should().Be(task1.Id);

		// Get tasks for list2
		var list2Tasks = await taskRepo.ListByTaskListAsync(list2.Id, true, ct);
		list2Tasks.Should().HaveCount(1);
		list2Tasks.First().Id.Should().Be(task2.Id);
	}

	[Fact]
	public async Task ListByFocusDateAsync_ReturnsTasksWithMatchingFocusDate()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var a = new TaskItem("a", list.Id); a.SetFocusAt(focusDate);
		var b = new TaskItem("b", list.Id); b.SetFocusAt(focusDate.AddDays(1));
		var c = new TaskItem("c", list.Id); c.SetFocusAt(focusDate);
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);
		await repo.AddAsync(c, ct);

		var result = await repo.ListByFocusDateAsync(focusDate, false, ct);
		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.FocusAt == focusDate);
	}

	[Fact]
	public async Task ListByFocusDateAsync_ExcludesCompletedTasksByDefault()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var a = new TaskItem("a", list.Id); a.SetFocusAt(focusDate);
		var b = new TaskItem("b", list.Id); b.SetFocusAt(focusDate); b.Complete();
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);

		var result = await repo.ListByFocusDateAsync(focusDate, false, ct);
		result.Should().HaveCount(1);
		result.First().Title.Should().Be("a");
	}

	[Fact]
	public async Task ListByFocusDateAsync_IncludesCompletedTasksWhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var a = new TaskItem("a", list.Id); a.SetFocusAt(focusDate);
		var b = new TaskItem("b", list.Id); b.SetFocusAt(focusDate); b.Complete();
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);

		var result = await repo.ListByFocusDateAsync(focusDate, true, ct);
		result.Should().HaveCount(2);
		result.Should().Contain(x => x.Title == "a");
		result.Should().Contain(x => x.Title == "b");
	}

	[Fact]
	public async Task ListByFocusDateAsync_NoTasksWithFocusDate_ReturnsEmpty()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var a = new TaskItem("a", list.Id); a.SetFocusAt(focusDate.AddDays(1));
		await repo.AddAsync(a, ct);

		var result = await repo.ListByFocusDateAsync(focusDate, false, ct);
		result.Should().BeEmpty();
	}

	[Fact]
	public async Task ListByGoalAsync_FiltersBy_GoalId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var otherGoalId = Guid.NewGuid();
		var a = new TaskItem("a", list.Id, goalId: goalId);
		var b = new TaskItem("b", list.Id, goalId: goalId);
		var c = new TaskItem("c", list.Id, goalId: otherGoalId);
		var d = new TaskItem("d", list.Id);
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);
		await repo.AddAsync(c, ct);
		await repo.AddAsync(d, ct);

		var result = await repo.ListByGoalAsync(goalId, true, ct);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.GoalId == goalId);
	}

	[Fact]
	public async Task ListByGoalAsync_ExcludesCompleted_WhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var a = new TaskItem("a", list.Id, goalId: goalId);
		var b = new TaskItem("b", list.Id, goalId: goalId);
		b.Complete();
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);

		var result = await repo.ListByGoalAsync(goalId, false, ct);

		result.Should().HaveCount(1);
		result.First().Title.Should().Be("a");
	}

	[Fact]
	public async Task ListByGoalAsync_IncludesCompleted_WhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var a = new TaskItem("a", list.Id, goalId: goalId);
		var b = new TaskItem("b", list.Id, goalId: goalId);
		b.Complete();
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);

		var result = await repo.ListByGoalAsync(goalId, true, ct);

		result.Should().HaveCount(2);
	}

	[Fact]
	public async Task ListByGoalAsync_OrdersByUpdatedAtDesc()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var a = new TaskItem("a", list.Id, goalId: goalId);
		var b = new TaskItem("b", list.Id, goalId: goalId);
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);

		a.SetDescription("updated");
		await repo.UpdateAsync(a, ct);

		var result = await repo.ListByGoalAsync(goalId, true, ct);

		result.First().Id.Should().Be(a.Id);
	}

	[Fact]
	public async Task ListByGoalAsync_ReturnsEmpty_WhenNoMatchingGoal()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var a = new TaskItem("a", list.Id, goalId: Guid.NewGuid());
		await repo.AddAsync(a, ct);

		var result = await repo.ListByGoalAsync(Guid.NewGuid(), true, ct);

		result.Should().BeEmpty();
	}
}
