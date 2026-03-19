using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Contexts;
using OwnPlanner.Domain.Contexts;

namespace OwnPlanner.Application.Tests.Contexts;

public class PlanningContextServiceTests
{
	private readonly IPlanningContextRepository _repo = Substitute.For<IPlanningContextRepository>();
	private readonly IPlanningContextService _svc;

	public PlanningContextServiceTests() => _svc = new PlanningContextService(_repo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		var ct = TestContext.Current.CancellationToken;
		PlanningContext? captured = null;
		_repo.AddAsync(Arg.Do<PlanningContext>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("Health", ContextType.Area, "Physical wellbeing", "#00FF00", ct);

		await _repo.Received(1).AddAsync(Arg.Any<PlanningContext>(), ct);
		dto.Name.Should().Be("Health");
		dto.Type.Should().Be(ContextType.Area);
		dto.Description.Should().Be("Physical wellbeing");
		dto.Color.Should().Be("#00FF00");
		dto.Status.Should().Be(ContextStatus.Active);
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Q2 Launch", ContextType.Project);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(context.Id);
		dto.Name.Should().Be("Q2 Launch");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((PlanningContext?)null);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_MapsContexts()
	{
		var ct = TestContext.Current.CancellationToken;
		var contexts = new[]
		{
			new PlanningContext("Q2 Launch", ContextType.Project),
			new PlanningContext("Health", ContextType.Area)
		}.ToList();
		_repo.ListAsync(false, ct).Returns(contexts);

		var result = await _svc.ListAsync(false, ct);

		result.Should().HaveCount(2);
		result.Select(x => x.Name).Should().Contain(["Q2 Launch", "Health"]);
	}

	[Fact]
	public async Task ListAsync_PassesIncludeArchivedToRepository()
	{
		var ct = TestContext.Current.CancellationToken;
		_repo.ListAsync(true, ct).Returns([]);

		await _svc.ListAsync(true, ct);

		await _repo.Received(1).ListAsync(true, ct);
	}

	[Fact]
	public async Task UpdateAsync_Throws_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((PlanningContext?)null);

		var act = async () => await _svc.UpdateAsync(id, name: "New Name", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>();
	}

	[Fact]
	public async Task UpdateAsync_UpdatesName()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Old Name", ContextType.Area);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.UpdateAsync(id, name: "New Name", ct: ct);

		dto.Name.Should().Be("New Name");
		await _repo.Received(1).UpdateAsync(context, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesType()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Work", ContextType.Area);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.UpdateAsync(id, type: ContextType.Project, ct: ct);

		dto.Type.Should().Be(ContextType.Project);
		await _repo.Received(1).UpdateAsync(context, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesStatus_ToArchived()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Work", ContextType.Project);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.UpdateAsync(id, status: ContextStatus.Archived, ct: ct);

		dto.Status.Should().Be(ContextStatus.Archived);
		await _repo.Received(1).UpdateAsync(context, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesStatus_ToCompleted()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Q2 Launch", ContextType.Project);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.UpdateAsync(id, status: ContextStatus.Completed, ct: ct);

		dto.Status.Should().Be(ContextStatus.Completed);
		await _repo.Received(1).UpdateAsync(context, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesColor()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Health", ContextType.Area);
		_repo.GetAsync(id, ct).Returns(context);

		var dto = await _svc.UpdateAsync(id, color: "#FF5733", ct: ct);

		dto.Color.Should().Be("#FF5733");
		await _repo.Received(1).UpdateAsync(context, ct);
	}

	[Fact]
	public async Task DeleteAsync_DeletesContext()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var context = new PlanningContext("Work", ContextType.Project);
		_repo.GetAsync(id, ct).Returns(context);

		await _svc.DeleteAsync(id, ct);

		await _repo.Received(1).DeleteAsync(context, ct);
	}

	[Fact]
	public async Task DeleteAsync_Throws_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((PlanningContext?)null);

		var act = async () => await _svc.DeleteAsync(id, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>();
	}
}
