using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class TaskGoalLinkServiceTests
{
	private readonly ITaskItemRepository _tasks = Substitute.For<ITaskItemRepository>();
	private readonly ITaskListRepository _lists = Substitute.For<ITaskListRepository>();
	private readonly IGoalRepository _goals = Substitute.For<IGoalRepository>();
	private readonly TaskList _list = new("Training");
	private readonly Goal _goal = new("Run a half marathon", GoalHorizon.Quarterly, targetPeriod: "2026-Q4");
	private TaskGoalLinkService Service => new(new TaskItemService(_tasks, _lists), _goals, _lists);

	private TaskItem Seed(Guid? goalId = null)
	{
		var task = new TaskItem("Book physiotherapy", _list.Id, "Knee assessment", DateTime.UtcNow.AddDays(2), true, goalId);
		task.SetFocusAt(DateTime.UtcNow.AddDays(1));
		_tasks.GetAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);
		_lists.GetAsync(_list.Id, Arg.Any<CancellationToken>()).Returns(_list);
		_goals.GetAsync(_goal.Id, Arg.Any<CancellationToken>()).Returns(_goal);
		return task;
	}

	[Fact]
	public async Task LinkChangesOnlyAssociationAndRepeatedLinkDoesNotWrite()
	{
		var task = Seed();
		var before = (await new TaskItemService(_tasks, _lists).GetAsync(task.Id, TestContext.Current.CancellationToken))!;
		var linked = await Service.LinkAsync(task.Id, _goal.Id, TestContext.Current.CancellationToken);
		linked.Should().BeEquivalentTo(before, options => options.Excluding(t => t.GoalId).Excluding(t => t.UpdatedAt));
		linked.GoalId.Should().Be(_goal.Id);
		var repeated = await Service.LinkAsync(task.Id, _goal.Id, TestContext.Current.CancellationToken);
		repeated.Should().BeEquivalentTo(linked);
		await _tasks.Received(1).UpdateAsync(task, Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(GoalStatus.Paused)]
	[InlineData(GoalStatus.Achieved)]
	[InlineData(GoalStatus.Dropped)]
	public async Task InactiveGoalIsRejectedEvenWhenItWasActiveAtSuggestionTime(GoalStatus status)
	{
		var task = Seed();
		_goal.SetStatus(status);
		var act = () => Service.LinkAsync(task.Id, _goal.Id, TestContext.Current.CancellationToken);
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only active goals*");
		task.GoalId.Should().BeNull();
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ConflictingLinkIsPreserved()
	{
		var existing = Guid.NewGuid();
		var task = Seed(existing);
		var act = () => Service.LinkAsync(task.Id, _goal.Id, TestContext.Current.CancellationToken);
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already linked*");
		task.GoalId.Should().Be(existing);
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData("task")]
	[InlineData("goal")]
	[InlineData("list")]
	[InlineData("archived")]
	public async Task MissingOrUnavailableTargetNeverWrites(string unavailable)
	{
		var task = Seed();
		if (unavailable == "task") _tasks.GetAsync(task.Id, Arg.Any<CancellationToken>()).Returns((TaskItem?)null);
		if (unavailable == "goal") _goals.GetAsync(_goal.Id, Arg.Any<CancellationToken>()).Returns((Goal?)null);
		if (unavailable == "list") _lists.GetAsync(_list.Id, Arg.Any<CancellationToken>()).Returns((TaskList?)null);
		if (unavailable == "archived") _list.Archive();
		var act = () => Service.LinkAsync(task.Id, _goal.Id, TestContext.Current.CancellationToken);
		await act.Should().ThrowAsync<Exception>().Where(ex => ex is KeyNotFoundException || ex is InvalidOperationException);
		task.GoalId.Should().BeNull();
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}
}
