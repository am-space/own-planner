using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public class TaskItemServiceTests
{
	private readonly ITaskItemRepository _repo = Substitute.For<ITaskItemRepository>();
	private readonly ITaskListRepository _taskListRepo = Substitute.For<ITaskListRepository>();
	private readonly ITaskItemService _svc;

	public TaskItemServiceTests() => _svc = new TaskItemService(_repo, _taskListRepo);

	[Fact]
	public async Task DeleteAsync_TrashesTaskAndIsIdempotent()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var item = new TaskItem("task", Guid.NewGuid());
		_repo.GetAsync(id, ct).Returns(item, (TaskItem?)null);
		_repo.GetTrashedAsync(id, ct).Returns(item);

		await _svc.DeleteAsync(id, ct);
		var trashedAt = item.TrashedAt;
		await _svc.DeleteAsync(id, ct);

		item.TrashedAt.Should().Be(trashedAt);
		item.ActiveTaskListId.Should().BeNull();
		await _repo.Received(2).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task RestoreAsync_RestoresToOriginalList()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("task", listId);
		item.Trash();
		_repo.GetTrashedAsync(id, ct).Returns(item);
		_taskListRepo.GetAsync(listId, ct).Returns(new TaskList("list"));

		await _svc.RestoreAsync(id, ct);

		item.TrashedAt.Should().BeNull();
		item.ActiveTaskListId.Should().Be(listId);
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task RestoreAsync_WhenOriginalListIsMissing_FailsWithoutChangingTask()
	{
		var ct = TestContext.Current.CancellationToken;
		var item = new TaskItem("task", Guid.NewGuid());
		item.Trash();
		_repo.GetTrashedAsync(item.Id, ct).Returns(item);

		var act = () => _svc.RestoreAsync(item.Id, ct);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*original task list*");
		item.TrashedAt.Should().NotBeNull();
		await _repo.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task PermanentlyDeleteAsync_RequiresTrashedTask()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();

		var act = () => _svc.PermanentlyDeleteAsync(id, ct);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in Trash*");
		await _repo.DidNotReceive().PermanentlyDeleteAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		var ct = TestContext.Current.CancellationToken;
		TaskItem? captured = null;
		var listId = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		_taskListRepo.GetAsync(listId, ct).Returns(taskList);
		_repo.AddAsync(Arg.Do<TaskItem>(x => captured = x), ct)
		.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("title", listId, "desc", ct: ct);

		await _repo.Received(1).AddAsync(Arg.Any<TaskItem>(), ct);
		dto.Title.Should().Be("title");
		dto.Description.Should().Be("desc");
		dto.TaskListId.Should().Be(listId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task CreateAsync_ThrowsKeyNotFoundException_WhenTaskListNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		_taskListRepo.GetAsync(listId, ct).Returns((TaskList?)null);

		var act = async () => await _svc.CreateAsync("title", listId, "desc", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"TaskList {listId} not found");
	}

	[Fact]
	public async Task CompleteAsync_Gets_Updates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("x", listId);
		_repo.GetAsync(id, ct).Returns(item);

		await _svc.CompleteAsync(id, ct);

		item.IsCompleted.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenTaskListNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var taskId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var item = new TaskItem("x", oldListId);
		_repo.GetAsync(taskId, ct).Returns(item);
		_taskListRepo.GetAsync(newListId, ct).Returns((TaskList?)null);

		var act = async () => await _svc.AssignToListAsync(taskId, newListId, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"TaskList {newListId} not found");
	}

	[Fact]
	public async Task ListAsync_Maps_Items()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var items = new[] { new TaskItem("a", listId), new TaskItem("b", listId) }.ToList();
		_repo.ListAsync(true, ct).Returns(items);

		var list = await _svc.ListAsync(true, ct);

		list.Should().HaveCount(2);
		list.Select(x => x.Title).Should().Contain(["a", "b"]);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Old Title", listId);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDescription()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId, "Old Description");
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, description: "New Description", ct: ct);

		dto.Description.Should().Be("New Description");
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDueAt()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		var dueDate = new DateTime(2024, 12, 31);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, dueAt: dueDate, ct: ct);

		dto.DueAt.Should().NotBeNull();
		dto.DueAt!.Value.Year.Should().Be(2024);
		dto.DueAt.Value.Month.Should().Be(12);
		dto.DueAt.Value.Day.Should().Be(31);
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Old Title", listId, "Old Description");
		var dueDate = new DateTime(2024, 12, 31);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Description", dueDate, ct: ct);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.DueAt.Should().NotBeNull();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Original Title", listId, "Original Description");
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("Original Description");
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenTaskNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((TaskItem?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Task {id} not found");
	}

	[Fact]
	public async Task CreateAsync_Adds_And_Maps_IsImportant()
	{
		var ct = TestContext.Current.CancellationToken;
		TaskItem? captured = null;
		var listId = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		_taskListRepo.GetAsync(listId, ct).Returns(taskList);
		_repo.AddAsync(Arg.Do<TaskItem>(x => captured = x), ct)
		.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("title", listId, "desc", isImportant: true, ct: ct);

		dto.IsImportant.Should().BeTrue();
		captured.Should().NotBeNull();
		captured!.IsImportant.Should().BeTrue();
	}

	[Fact]
	public async Task UpdateAsync_UpdatesIsImportant()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, isImportant: true, ct: ct);

		dto.IsImportant.Should().BeTrue();
		item.IsImportant.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task SetFocusDateAsync_SetsFocusAtAndUpdates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		_repo.GetAsync(id, ct).Returns(item);
		var focusDate = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc);

		await _svc.SetFocusDateAsync(id, focusDate, ct);

		item.FocusAt.Should().Be(focusDate);
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task ClearFocusDateAsync_ClearsFocusAtAndUpdates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		item.SetFocusAt(DateTime.UtcNow);
		_repo.GetAsync(id, ct).Returns(item);

		await _svc.ClearFocusDateAsync(id, ct);

		item.FocusAt.Should().BeNull();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task ListByFocusDateAsync_ReturnsTasksWithFocusDate()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var items = new[]
		{
			new TaskItem("a", listId) { },
			new TaskItem("b", listId) { }
		};
		items[0].SetFocusAt(focusDate);
		items[1].SetFocusAt(focusDate.AddDays(1));
		_repo.ListByFocusDateAsync(focusDate, false, ct).Returns([items[0]]);

		var result = await _svc.ListByFocusDateAsync(focusDate, ct: ct);

		result.Should().HaveCount(1);
		result[0].Title.Should().Be("a");
		result[0].FocusAt.Should().Be(focusDate);
	}

	[Fact]
	public async Task Map_IncludesFocusAt()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var focusDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var item = new TaskItem("Title", listId);
		item.SetFocusAt(focusDate);
		_repo.GetAsync(item.Id, ct).Returns(item);

		var dto = await _svc.GetAsync(item.Id, ct);
		dto!.FocusAt.Should().Be(focusDate);
	}

	[Fact]
	public async Task CreateAsync_SetsGoalId_InDto()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		_taskListRepo.GetAsync(listId, ct).Returns(taskList);
		_repo.AddAsync(Arg.Any<TaskItem>(), ct).Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("title", listId, goalId: goalId, ct: ct);

		dto.GoalId.Should().Be(goalId);
	}

	[Fact]
	public async Task UpdateAsync_SetsGoalId()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, goalId: goalId, ct: ct);

		item.GoalId.Should().Be(goalId);
		dto.GoalId.Should().Be(goalId);
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_ClearsGoalId_WhenClearGoalIdIsTrue()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var item = new TaskItem("Title", listId, goalId: goalId);
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, clearGoalId: true, ct: ct);

		item.GoalId.Should().BeNull();
		dto.GoalId.Should().BeNull();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task UpdateAsync_ClearGoalId_TakesPrecedenceOver_GoalId()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var newGoalId = Guid.NewGuid();
		var item = new TaskItem("Title", listId, goalId: Guid.NewGuid());
		_repo.GetAsync(id, ct).Returns(item);

		var dto = await _svc.UpdateAsync(id, goalId: newGoalId, clearGoalId: true, ct: ct);

		item.GoalId.Should().BeNull();
		dto.GoalId.Should().BeNull();
		await _repo.Received(1).UpdateAsync(item, ct);
	}

	[Fact]
	public async Task ListByGoalAsync_DelegatesToRepo_AndMapsResults()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var goalId = Guid.NewGuid();
		var items = new[]
		{
			new TaskItem("a", listId, goalId: goalId),
			new TaskItem("b", listId, goalId: goalId)
		}.ToList();
		_repo.ListByGoalAsync(goalId, true, ct).Returns(items);

		var result = await _svc.ListByGoalAsync(goalId, ct: ct);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(x => x.GoalId == goalId);
	}

	[Fact]
	public async Task ListPagedAsync_MapsItems_AndComputesPaging()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var items = new[] { new TaskItem("a", listId), new TaskItem("b", listId) }.ToList();
		_repo.ListPagedAsync(false, false, 0, 25, ct).Returns((items, 10));

		var page = await _svc.ListPagedAsync(false, false, 0, 25, ct);

		page.Items.Should().HaveCount(2);
		page.TotalCount.Should().Be(10);
		page.Offset.Should().Be(0);
		page.Limit.Should().Be(25);
		page.HasMore.Should().BeTrue(); // 0 + 2 < 10
	}

	[Fact]
	public async Task ListPagedAsync_HasMoreFalse_WhenPageReachesEnd()
	{
		var ct = TestContext.Current.CancellationToken;
		var listId = Guid.NewGuid();
		var items = new[] { new TaskItem("a", listId), new TaskItem("b", listId) }.ToList();
		_repo.ListPagedAsync(false, false, 8, 25, ct).Returns((items, 10));

		var page = await _svc.ListPagedAsync(false, false, 8, 25, ct);

		page.HasMore.Should().BeFalse(); // 8 + 2 == 10
	}

	[Fact]
	public async Task ListPagedAsync_ClampsLimitToMax()
	{
		var ct = TestContext.Current.CancellationToken;
		_repo.ListPagedAsync(false, false, 0, TaskItemService.MaxPageLimit, ct)
			.Returns(([], 0));

		var page = await _svc.ListPagedAsync(false, false, 0, 5000, ct);

		page.Limit.Should().Be(TaskItemService.MaxPageLimit);
		await _repo.Received(1).ListPagedAsync(false, false, 0, TaskItemService.MaxPageLimit, ct);
	}

	[Fact]
	public async Task ListPagedAsync_NonPositiveLimit_FallsBackToDefault()
	{
		var ct = TestContext.Current.CancellationToken;
		_repo.ListPagedAsync(false, false, 0, TaskItemService.DefaultPageLimit, ct)
			.Returns(([], 0));

		var page = await _svc.ListPagedAsync(false, false, 0, 0, ct);

		page.Limit.Should().Be(TaskItemService.DefaultPageLimit);
	}

	[Fact]
	public async Task ListPagedAsync_FloorsNegativeOffsetToZero()
	{
		var ct = TestContext.Current.CancellationToken;
		_repo.ListPagedAsync(false, false, 0, 25, ct).Returns(([], 0));

		var page = await _svc.ListPagedAsync(false, false, -5, 25, ct);

		page.Offset.Should().Be(0);
		await _repo.Received(1).ListPagedAsync(false, false, 0, 25, ct);
	}
}
