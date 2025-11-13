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

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Old Title", listId);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDescription()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId, "Old Description");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		var dto = await _svc.UpdateAsync(id, description: "New Description");

		dto.Description.Should().Be("New Description");
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesDueAt()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Title", listId);
		var dueDate = new DateTime(2024, 12, 31);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		var dto = await _svc.UpdateAsync(id, dueAt: dueDate);

		dto.DueAt.Should().NotBeNull();
		dto.DueAt!.Value.Year.Should().Be(2024);
		dto.DueAt.Value.Month.Should().Be(12);
		dto.DueAt.Value.Day.Should().Be(31);
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMultipleFields()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Old Title", listId, "Old Description");
		var dueDate = new DateTime(2024, 12, 31);
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Description", dueDate);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.DueAt.Should().NotBeNull();
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_OnlyUpdatesProvidedFields()
	{
		var id = Guid.NewGuid();
		var listId = Guid.NewGuid();
		var item = new TaskItem("Original Title", listId, "Original Description");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(item);

		var dto = await _svc.UpdateAsync(id, title: "New Title");

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("Original Description");
		await _repo.Received(1).UpdateAsync(item, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpdateAsync_ThrowsKeyNotFoundException_WhenTaskNotFound()
	{
		var id = Guid.NewGuid();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns((TaskItem?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title");

		await act.Should().ThrowAsync<KeyNotFoundException>()
			.WithMessage($"Task {id} not found");
	}
}
