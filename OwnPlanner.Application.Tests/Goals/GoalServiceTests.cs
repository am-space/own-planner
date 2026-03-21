using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Goals;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Tests.Goals;

public class GoalServiceTests
{
	private readonly IGoalRepository _repo = Substitute.For<IGoalRepository>();
	private readonly IGoalService _svc;

	public GoalServiceTests() => _svc = new GoalService(_repo);

	[Fact]
	public async Task CreateAsync_Adds_And_Maps()
	{
		var ct = TestContext.Current.CancellationToken;
		Goal? captured = null;
		_repo.AddAsync(Arg.Do<Goal>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("Q2 Launch", GoalHorizon.Quarterly, "Big launch", "2025-Q2", null, "Ship v1.0", ct);

		await _repo.Received(1).AddAsync(Arg.Any<Goal>(), ct);
		dto.Title.Should().Be("Q2 Launch");
		dto.Description.Should().Be("Big launch");
		dto.Horizon.Should().Be(GoalHorizon.Quarterly);
		dto.TargetPeriod.Should().Be("2025-Q2");
		dto.TargetDate.Should().BeNull();
		dto.Status.Should().Be(GoalStatus.Active);
		dto.Metric.Should().Be("Ship v1.0");
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task CreateAsync_WithTargetDateHorizon_Adds_And_Maps()
	{
		var ct = TestContext.Current.CancellationToken;
		var deadline = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
		Goal? captured = null;
		_repo.AddAsync(Arg.Do<Goal>(x => captured = x), ct)
			.Returns(Task.CompletedTask);

		var dto = await _svc.CreateAsync("Finish book", GoalHorizon.TargetDate, targetDate: deadline, ct: ct);

		dto.Horizon.Should().Be(GoalHorizon.TargetDate);
		dto.TargetDate.Should().Be(deadline);
		dto.TargetPeriod.Should().BeNull();
		captured.Should().NotBeNull();
		dto.Id.Should().Be(captured!.Id);
	}

	[Fact]
	public async Task GetAsync_ReturnsDto_WhenFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Health", GoalHorizon.Yearly, targetPeriod: "2025");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().NotBeNull();
		dto!.Id.Should().Be(goal.Id);
		dto.Title.Should().Be("Health");
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((Goal?)null);

		var dto = await _svc.GetAsync(id, ct);

		dto.Should().BeNull();
	}

	[Fact]
	public async Task ListAsync_MapsGoals()
	{
		var ct = TestContext.Current.CancellationToken;
		var goals = new[]
		{
			new Goal("Health", GoalHorizon.Quarterly, targetPeriod: "2025-Q2"),
			new Goal("Finance", GoalHorizon.Yearly, targetPeriod: "2025")
		}.ToList();
		_repo.ListAsync(false, ct).Returns(goals);

		var result = await _svc.ListAsync(false, ct);

		result.Should().HaveCount(2);
		result.Select(x => x.Title).Should().Contain(["Health", "Finance"]);
	}

	[Fact]
	public async Task ListAsync_PassesIncludeInactiveToRepository()
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
		_repo.GetAsync(id, ct).Returns((Goal?)null);

		var act = async () => await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		await act.Should().ThrowAsync<KeyNotFoundException>();
	}

	[Fact]
	public async Task UpdateAsync_UpdatesTitle()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Old Title", GoalHorizon.Monthly, targetPeriod: "2025-01");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.UpdateAsync(id, title: "New Title", ct: ct);

		dto.Title.Should().Be("New Title");
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesStatus()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Quarterly, targetPeriod: "2025-Q2");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.UpdateAsync(id, status: GoalStatus.Achieved, ct: ct);

		dto.Status.Should().Be(GoalStatus.Achieved);
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	[Fact]
	public async Task UpdateAsync_UpdatesMetricCurrent()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Monthly, metric: "Run 5km", targetPeriod: "2025-01");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.UpdateAsync(id, metricCurrent: "3.2 km", ct: ct);

		dto.MetricCurrent.Should().Be("3.2 km");
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	// Horizon trio: changing only horizon keeps the existing targetPeriod
	[Fact]
	public async Task UpdateAsync_UpdatesHorizon_KeepsExistingTargetPeriod()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Monthly, targetPeriod: "2025-01");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.UpdateAsync(id, horizon: GoalHorizon.Quarterly, ct: ct);

		dto.Horizon.Should().Be(GoalHorizon.Quarterly);
		dto.TargetPeriod.Should().Be("2025-01");
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	// Horizon trio: changing only targetPeriod keeps the existing horizon
	[Fact]
	public async Task UpdateAsync_UpdatesTargetPeriod_KeepsExistingHorizon()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Quarterly, targetPeriod: "2025-Q1");
		_repo.GetAsync(id, ct).Returns(goal);

		var dto = await _svc.UpdateAsync(id, targetPeriod: "2025-Q2", ct: ct);

		dto.TargetPeriod.Should().Be("2025-Q2");
		dto.Horizon.Should().Be(GoalHorizon.Quarterly);
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	// Horizon trio: switching to TargetDate clears targetPeriod
	[Fact]
	public async Task UpdateAsync_SwitchesToTargetDateHorizon_ClearsTargetPeriod()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Monthly, targetPeriod: "2025-01");
		_repo.GetAsync(id, ct).Returns(goal);
		var deadline = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

		var dto = await _svc.UpdateAsync(id, horizon: GoalHorizon.TargetDate, targetDate: deadline, ct: ct);

		dto.Horizon.Should().Be(GoalHorizon.TargetDate);
		dto.TargetDate.Should().Be(deadline);
		dto.TargetPeriod.Should().BeNull();
		await _repo.Received(1).UpdateAsync(goal, ct);
	}

	[Fact]
	public async Task DeleteAsync_DeletesGoal()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		var goal = new Goal("Title", GoalHorizon.Monthly, targetPeriod: "2025-01");
		_repo.GetAsync(id, ct).Returns(goal);

		await _svc.DeleteAsync(id, ct);

		await _repo.Received(1).DeleteAsync(goal, ct);
	}

	[Fact]
	public async Task DeleteAsync_Throws_WhenNotFound()
	{
		var ct = TestContext.Current.CancellationToken;
		var id = Guid.NewGuid();
		_repo.GetAsync(id, ct).Returns((Goal?)null);

		var act = async () => await _svc.DeleteAsync(id, ct);

		await act.Should().ThrowAsync<KeyNotFoundException>();
	}
}
