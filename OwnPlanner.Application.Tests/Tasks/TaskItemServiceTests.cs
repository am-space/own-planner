using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public class TaskItemServiceTests
{
	private readonly ITaskItemRepository _repo = Substitute.For<ITaskItemRepository>();
	private readonly ITaskItemService _svc;

	public TaskItemServiceTests() => _svc = new TaskItemService(_repo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		TaskItem? captured = null;
		var listId = Guid.NewGuid();
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
