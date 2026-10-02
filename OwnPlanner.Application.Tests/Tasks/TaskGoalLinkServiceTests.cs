using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class TaskGoalLinkServiceTests
{
	private readonly ITaskItemRepository _tasks = Substitute.For<ITaskItemRepository>();
	private TaskGoalLinkService Service => new(_tasks);

	[Theory]
	[InlineData(TaskGoalLinkStatus.Linked)]
	[InlineData(TaskGoalLinkStatus.AlreadyLinked)]
	public async Task ReturnsAtomicRepositorySnapshotWithoutAnOrdinaryUpdate(TaskGoalLinkStatus status)
	{
		var ct = TestContext.Current.CancellationToken;
		var goal = Guid.NewGuid();
		var task = new TaskItem("Book physiotherapy", Guid.NewGuid(), "Knee assessment", DateTime.UtcNow.AddDays(2), true, goal);
		task.SetFocusAt(DateTime.UtcNow.AddDays(1));
		_tasks.LinkToActiveGoalAsync(task.Id, goal, ct).Returns(new TaskGoalLinkResult(status, task));
		var linked = await Service.LinkAsync(task.Id, goal, ct);
		linked.Id.Should().Be(task.Id);
		linked.GoalId.Should().Be(goal);
		linked.Title.Should().Be(task.Title);
		linked.DueAt.Should().Be(task.DueAt);
		linked.FocusAt.Should().Be(task.FocusAt);
		await _tasks.Received(1).LinkToActiveGoalAsync(task.Id, goal, ct);
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
		await _tasks.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(TaskGoalLinkStatus.TaskNotFound, "Task not found")]
	[InlineData(TaskGoalLinkStatus.GoalNotFound, "Goal not found")]
	[InlineData(TaskGoalLinkStatus.TaskListUnavailable, "The task's list is unavailable or archived.")]
	[InlineData(TaskGoalLinkStatus.GoalInactive, "Only active goals can be linked through this operation.")]
	[InlineData(TaskGoalLinkStatus.ConflictingLink, "The task is already linked to another goal. Ask the user before replacing it.")]
	public async Task MapsAtomicRejectionWithoutFallingBackToUnguardedUpdate(TaskGoalLinkStatus status, string message)
	{
		var task = Guid.NewGuid(); var goal = Guid.NewGuid(); var ct = TestContext.Current.CancellationToken;
		_tasks.LinkToActiveGoalAsync(task, goal, ct).Returns(new TaskGoalLinkResult(status));
		var act = () => Service.LinkAsync(task, goal, ct);
		await act.Should().ThrowAsync<Exception>().WithMessage(message);
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task PropagatesCancellationWithoutRetryingAnOrdinaryUpdate()
	{
		using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
		var id = Guid.NewGuid(); var goal = Guid.NewGuid();
		_tasks.LinkToActiveGoalAsync(id, goal, cancellation.Token).Returns(Task.FromCanceled<TaskGoalLinkResult>(cancellation.Token));
		var act = () => Service.LinkAsync(id, goal, cancellation.Token);
		await act.Should().ThrowAsync<OperationCanceledException>();
		await _tasks.DidNotReceive().UpdateAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
	}
}
