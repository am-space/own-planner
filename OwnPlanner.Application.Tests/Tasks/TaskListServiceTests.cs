using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public class TaskListServiceTests
{
	private readonly ITaskListRepository _repo = Substitute.For<ITaskListRepository>();
	private readonly ITaskListService _svc;

	public TaskListServiceTests() => _svc = new TaskListService(_repo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		TaskList? captured = null;
		_repo.AddAsync(Arg.Do<TaskList>(x => captured = x), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("Shopping List", "Weekly groceries", "#FF5733");

		await _repo.Received(1).AddAsync(Arg.Any<TaskList>(), Arg.Any<CancellationToken>());
		dto.Title.Should().Be("Shopping List");
		dto.Description.Should().Be("Weekly groceries");
		dto.Color.Should().Be("#FF5733");
		dto.IsArchived.Should().BeFalse();
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task ArchiveAsync_Gets_Updates()
	{
		var id = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(taskList);

		await _svc.ArchiveAsync(id);

		taskList.IsArchived.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(taskList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UnarchiveAsync_Gets_Updates()
	{
		var id = Guid.NewGuid();
		var taskList = new TaskList("Test List");
		taskList.Archive();
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(taskList);

		await _svc.UnarchiveAsync(id);

		taskList.IsArchived.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(taskList, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListAsync_Maps_Lists()
	{
		var lists = new[] { new TaskList("Personal"), new TaskList("Work") }.ToList();
		_repo.ListAsync(false, Arg.Any<CancellationToken>()).Returns(lists);

		var result = await _svc.ListAsync(false);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Personal", "Work"]);
	}

	[Fact]
	public async Task UpdateAsync_Updates_Properties()
	{
		var id = Guid.NewGuid();
		var taskList = new TaskList("Old Title");
		_repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(taskList);

		var dto = await _svc.UpdateAsync(id, "New Title", "New Description", "#00FF00");

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.Color.Should().Be("#00FF00");
		await _repo.Received(1).UpdateAsync(taskList, Arg.Any<CancellationToken>());
	}
}
