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
	public async Task Add_Get_Update_PermanentDelete_Roundtrip()
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

		loaded.Trash();
		await repo.UpdateAsync(loaded, ct);
		(await repo.PermanentlyDeleteAsync(loaded.Id, ct)).Should().Be(TaskPermanentDeleteResult.Deleted);
		(await repo.GetAsync(item.Id, ct)).Should().BeNull();
	}

	[Fact]
	public async Task RestoreAndPermanentDelete_ApplyOnlyToCurrentTrashedState()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var factory = new TestPlannerDbContextFactory(conn);
		var tasks = new TaskItemRepository(factory);
		var lists = new TaskListRepository(factory);
		var list = new TaskList("list");
		await lists.AddAsync(list, ct);
		var task = new TaskItem("task", list.Id);
		await tasks.AddAsync(task, ct);

		(await tasks.PermanentlyDeleteAsync(task.Id, ct)).Should().Be(TaskPermanentDeleteResult.TaskNotTrashed);
		task.Trash();
		await tasks.UpdateAsync(task, ct);
		(await tasks.RestoreAsync(task.Id, ct)).Should().Be(TaskRestoreResult.Restored);
		(await tasks.PermanentlyDeleteAsync(task.Id, ct)).Should().Be(TaskPermanentDeleteResult.TaskNotTrashed);
	}

	[Fact]
	public async Task TrashedTask_IsExcludedFromNormalReads_AndSurvivesListDeletion()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var factory = new TestPlannerDbContextFactory(conn);
		var tasks = new TaskItemRepository(factory);
		var lists = new TaskListRepository(factory);
		var list = new TaskList("list");
		await lists.AddAsync(list, ct);
		var goalId = Guid.NewGuid();
		var focusDate = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
		var task = new TaskItem("recoverable", list.Id, isImportant: true, goalId: goalId);
		task.SetFocusAt(focusDate);
		await tasks.AddAsync(task, ct);
		task.Trash();
		await tasks.UpdateAsync(task, ct);

		(await tasks.GetAsync(task.Id, ct)).Should().BeNull();
		(await tasks.ListAsync(true, ct)).Should().BeEmpty();
		(await tasks.ListByTaskListAsync(list.Id, true, ct)).Should().BeEmpty();
		(await tasks.ListByGoalAsync(goalId, true, ct)).Should().BeEmpty();
		(await tasks.ListByFocusDateAsync(focusDate, true, ct)).Should().BeEmpty();
		var allPage = await tasks.ListPagedAsync(true, false, 0, 25, ct);
		allPage.Items.Should().BeEmpty();
		allPage.TotalCount.Should().Be(0);
		var listPage = await tasks.ListByTaskListPagedAsync(list.Id, true, false, 0, 25, ct);
		listPage.Items.Should().BeEmpty();
		listPage.TotalCount.Should().Be(0);
		var goalPage = await tasks.ListByGoalPagedAsync(goalId, true, 0, 25, ct);
		goalPage.Items.Should().BeEmpty();
		goalPage.TotalCount.Should().Be(0);
		var focusPage = await tasks.ListByFocusDatePagedAsync(focusDate, true, 0, 25, ct);
		focusPage.Items.Should().BeEmpty();
		focusPage.TotalCount.Should().Be(0);
		var trash = await tasks.ListTrashedPagedAsync(0, 25, ct);
		trash.Items.Should().ContainSingle().Which.TaskListId.Should().Be(list.Id);

		await lists.DeleteAsync(list, ct);

		var retained = await tasks.GetTrashedAsync(task.Id, ct);
		retained.Should().NotBeNull();
		retained!.TaskListId.Should().Be(list.Id);
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

	[Fact]
	public async Task ListPagedAsync_OrdersByFocusDateAscending_WithNullsLast()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var backlog = new TaskItem("backlog", list.Id);                 // no focus date → last
		var later = new TaskItem("later", list.Id);
		later.SetFocusAt(new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc));
		var soon = new TaskItem("soon", list.Id);
		soon.SetFocusAt(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
		// Insert in non-sorted order to prove ordering is applied, not insertion order.
		await repo.AddAsync(backlog, ct);
		await repo.AddAsync(later, ct);
		await repo.AddAsync(soon, ct);

		var (items, total) = await repo.ListPagedAsync(includeCompleted: true, onlyImportant: false, offset: 0, limit: 25, ct);

		total.Should().Be(3);
		items.Select(t => t.Title).Should().ContainInOrder("soon", "later", "backlog");
	}

	[Fact]
	public async Task ListPagedAsync_SameFocusDate_OrdersByUpdatedAtDescending()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var focus = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc);
		var first = new TaskItem("first", list.Id);
		var second = new TaskItem("second", list.Id);
		first.SetFocusAt(focus);
		second.SetFocusAt(focus);
		await repo.AddAsync(first, ct);
		await repo.AddAsync(second, ct);

		// Touch 'first' last so it has the most recent UpdatedAt.
		first.SetDescription("touched");
		await repo.UpdateAsync(first, ct);

		var (items, _) = await repo.ListPagedAsync(includeCompleted: true, onlyImportant: false, offset: 0, limit: 25, ct);

		items.Select(t => t.Title).Should().ContainInOrder("first", "second");
	}

	[Fact]
	public async Task ListPagedAsync_Paginates_WithoutSkippingOrRepeating()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		for (var i = 0; i < 10; i++)
			await repo.AddAsync(new TaskItem($"task-{i}", list.Id), ct);

		var page1 = await repo.ListPagedAsync(true, false, offset: 0, limit: 4, ct);
		var page2 = await repo.ListPagedAsync(true, false, offset: 4, limit: 4, ct);
		var page3 = await repo.ListPagedAsync(true, false, offset: 8, limit: 4, ct);

		page1.TotalCount.Should().Be(10);
		page1.Items.Should().HaveCount(4);
		page2.Items.Should().HaveCount(4);
		page3.Items.Should().HaveCount(2);

		var all = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(t => t.Id).ToList();
		all.Should().OnlyHaveUniqueItems();
		all.Should().HaveCount(10);
	}

	[Fact]
	public async Task ListPagedAsync_FiltersOnlyImportant_AndExcludesCompleted()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new TaskItemRepository(dbContextFactory);
		var listRepo = new TaskListRepository(dbContextFactory);

		var list = new TaskList("Test List");
		await listRepo.AddAsync(list, ct);

		var important = new TaskItem("important", list.Id, isImportant: true);
		var normal = new TaskItem("normal", list.Id);
		var importantDone = new TaskItem("important-done", list.Id, isImportant: true);
		importantDone.Complete();
		await repo.AddAsync(important, ct);
		await repo.AddAsync(normal, ct);
		await repo.AddAsync(importantDone, ct);

		var (items, total) = await repo.ListPagedAsync(includeCompleted: false, onlyImportant: true, offset: 0, limit: 25, ct);

		total.Should().Be(1);
		items.Should().ContainSingle(t => t.Title == "important");
	}
}
