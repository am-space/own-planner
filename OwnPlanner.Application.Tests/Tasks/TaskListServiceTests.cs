using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
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
		var ct = TestContext.Current.CancellationToken;
		TaskList? captured = null;
		_repo.AddAsync(Arg.Do<TaskList>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var contextId = Guid.NewGuid();
		var dto = await _svc.CreateAsync("Shopping List", contextId, "Weekly groceries", "#FF5733", ct);

		await _repo.Received(1).AddAsync(Arg.Any<TaskList>(), ct);
		dto.Title.Should().Be("Shopping List");
		dto.Description.Should().Be("Weekly groceries");
		dto.Color.Should().Be("#FF5733");
		dto.IsArchived.Should().BeFalse();
		dto.ContextId.Should().Be(contextId);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task ArchiveAsync_Gets_Updates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var taskList = new TaskList("Test List", contextId: Guid.NewGuid());
		_repo.GetAsync(id, ct).Returns(taskList);

		await _svc.ArchiveAsync(id, ct);

		taskList.IsArchived.Should().BeTrue();
		await _repo.Received(1).UpdateAsync(taskList, ct);
	}

	[Fact]
	public async Task UnarchiveAsync_Gets_Updates()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var taskList = new TaskList("Test List", contextId: Guid.NewGuid());
		taskList.Archive();
		_repo.GetAsync(id, ct).Returns(taskList);

		await _svc.UnarchiveAsync(id, ct);

		taskList.IsArchived.Should().BeFalse();
		await _repo.Received(1).UpdateAsync(taskList, ct);
	}

	[Fact]
	public async Task ListAsync_Maps_Lists()
	{
		var ct = TestContext.Current.CancellationToken;
		var ctxId = Guid.NewGuid();
		var lists = new[] { new TaskList("Personal", contextId: ctxId), new TaskList("Work", contextId: ctxId) }.ToList();
		_repo.ListAsync(false, null, false, ct).Returns(lists);

		var result = await _svc.ListAsync(false, ct: ct);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Personal", "Work"]);
	}

	[Fact]
	public async Task CreateAsync_EmptyContextId_ThrowsArgumentException()
	{
		var ct = TestContext.Current.CancellationToken;
		var act = async () => await _svc.CreateAsync("My List", Guid.Empty, ct: ct);

		await act.Should().ThrowAsync<ArgumentException>().WithParameterName("contextId");
	}

	[Fact]
	public async Task UpdateAsync_Updates_Properties()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var contextId = Guid.NewGuid();
		var taskList = new TaskList("Old Title", contextId: Guid.NewGuid());
		_repo.GetAsync(id, ct).Returns(taskList);

		var dto = await _svc.UpdateAsync(id, "New Title", contextId, "New Description", "#00FF00", ct);

		dto.Title.Should().Be("New Title");
		dto.Description.Should().Be("New Description");
		dto.Color.Should().Be("#00FF00");
		dto.ContextId.Should().Be(contextId);
		await _repo.Received(1).UpdateAsync(taskList, ct);
	}
}
