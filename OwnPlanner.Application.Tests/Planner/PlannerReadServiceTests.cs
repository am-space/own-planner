using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Planner;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Tests.Planner;

public class PlannerReadServiceTests
{
	private readonly IPlannerReadStore _store = Substitute.For<IPlannerReadStore>();

	[Fact]
	public async Task QueryTasksAsync_NormalizesSearch_AndDelegatesQuery()
	{
		var expected = new PagedResult<PlannerTaskSummaryDto>([], 0, 5, 10);
		_store.QueryTasksAsync(Arg.Any<PlannerTaskQuery>(), Arg.Any<CancellationToken>()).Returns(expected);
		var service = new PlannerReadService(_store);

		var result = await service.QueryTasksAsync(
			new PlannerTaskQuery(Search: "  launch  ", Offset: 5, Limit: 10),
			TestContext.Current.CancellationToken);

		result.Should().BeSameAs(expected);
		await _store.Received(1).QueryTasksAsync(
			Arg.Is<PlannerTaskQuery>(query => query.Search == "launch" && query.Offset == 5 && query.Limit == 10),
			Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(-1, 25)]
	[InlineData(0, 0)]
	[InlineData(0, 101)]
	public async Task QueryNotesAsync_InvalidPaging_Throws(int offset, int limit)
	{
		var service = new PlannerReadService(_store);

		var action = () => service.QueryNotesAsync(new PlannerNoteQuery(Offset: offset, Limit: limit));

		await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await _store.DidNotReceive().QueryNotesAsync(Arg.Any<PlannerNoteQuery>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task QueryTasksAsync_InvalidStatus_Throws()
	{
		var service = new PlannerReadService(_store);

		var action = () => service.QueryTasksAsync(new PlannerTaskQuery(Status: (PlannerTaskStatus)999));

		await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await _store.DidNotReceive().QueryTasksAsync(Arg.Any<PlannerTaskQuery>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task QueryGoalsAsync_InvalidHorizon_Throws()
	{
		var service = new PlannerReadService(_store);

		var action = () => service.QueryGoalsAsync(new PlannerGoalQuery(Horizon: (GoalHorizon)999));

		await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
		await _store.DidNotReceive().QueryGoalsAsync(Arg.Any<PlannerGoalQuery>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetTaskAsync_EmptyId_Throws()
	{
		var service = new PlannerReadService(_store);

		var action = () => service.GetTaskAsync(Guid.Empty);

		await action.Should().ThrowAsync<ArgumentException>();
		await _store.DidNotReceive().GetTaskAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}
}
