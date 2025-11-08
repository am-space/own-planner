using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public class TaskItemServiceTests
{
	private readonly ITaskItemRepository _repo = Substitute.For<ITaskItemRepository>();
	private readonly ITaskListRepository _taskListRepo = Substitute.For<ITaskListRepository>();
	private readonly ITaskItemService _svc;

	public TaskItemServiceTests() => _svc = new TaskItemService(_repo, _taskListRepo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		TaskItem? captured = null;
		var listId = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		_taskListRepo.GetAsync(listId, Arg.Any<CancellationToken>()).Returns(taskList);
		_repo.AddAsync(Arg.Do<TaskItem>(x => captured = x), Arg.Any<CancellationToken>())
		.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("title", listId, "desc");

		await _repo.Received(1).AddAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
		dto.Title.Should().Be("title");
		dto.Description.Should().Be("desc");
		dto.TaskListId.Should().Be(listId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task CreateAsync_ThrowsKeyNotFoundException_WhenTaskListNotFound()
	{
		var listId = Guid.NewGuid();
		_taskListRepo.GetAsync(listId, Arg.Any<CancellationToken>()).Returns((TaskList?)null);

		var act = async () => await _svc.CreateAsync("title", listId, "desc");

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"TaskList {listId} not found");
	}

	[Fact]
	public async Task CompleteAsync_Gets_Updates()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("x", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		await _svc.CompleteAsync(id);

		item.IsCompleted.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task AssignToListAsync_ThrowsKeyNotFoundException_WhenTaskListNotFound()
	{
		var taskId = Guid.NewGuid();
		var oldListId = Guid.NewGuid();
		var newListId = Guid.NewGuid();
		var item = new TaskItem("x", oldListId);
		_repo.GetAsync(taskId, Arg.Any<CancellationToken>()).Returns(item);
		_taskListRepo.GetAsync(newListId, Arg.Any<CancellationToken>()).Returns((TaskList?)null);

		var act = async () => await _svc.AssignToListAsync(taskId, newListId);

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"TaskList {newListId} not found");
	}

	[Fact]
	public async Task ListAsync_Maps_Items()
	{
		var listId = Guid.NewGuid();
		var items = new[] { new TaskItem("a", listId), new TaskItem("b", listId) }.ToList();
		_repo.ListAsync(true, Arg.Any<CancellationToken>()).Returns(items);

		var list = await _svc.ListAsync(true);

		list.Should().HaveCount(2);
		list.Select(x => x.Title).Should().Contain(["a", "b"]);
	}
}
