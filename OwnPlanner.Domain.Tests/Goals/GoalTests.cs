using FluentAssertions;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Domain.Tests.Goals;

public sealed class GoalTests
{
	[Fact]
	public void PauseAndResumePreserveFieldsAndRepeatedRequestsPreserveTimestamps()
	{
		var goal = new Goal("Spanish", GoalHorizon.Yearly, targetPeriod: "2026", metric: "B2");
		var paused = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
		goal.SetStatus(GoalStatus.Paused, paused);
		goal.SetStatus(GoalStatus.Paused, paused.AddDays(1));
		goal.PausedAt.Should().Be(paused);
		goal.Status.Should().Be(GoalStatus.Paused);
		goal.LastResumedAt.Should().BeNull();
		var resumed = paused.AddDays(30);
		goal.SetStatus(GoalStatus.Active, resumed);
		goal.SetStatus(GoalStatus.Active, resumed.AddDays(1));
		goal.PausedAt.Should().BeNull();
		goal.LastResumedAt.Should().Be(resumed);
		goal.TargetPeriod.Should().Be("2026");
		goal.Metric.Should().Be("B2");
	}

	[Theory]
	[InlineData(GoalStatus.Achieved)]
	[InlineData(GoalStatus.Dropped)]
	public void PauseDoesNotChangeFinishedGoals(GoalStatus status)
	{
		var goal = new Goal("Finished", GoalHorizon.Yearly);
		goal.SetStatus(status);
		var action = () => goal.SetStatus(GoalStatus.Paused);
		action.Should().Throw<ArgumentException>();
		goal.Status.Should().Be(status);
		goal.PausedAt.Should().BeNull();
	}

	[Fact]
	public void CompletingPausedGoalClearsPauseWithoutRecordingResume()
	{
		var goal = new Goal("Finished", GoalHorizon.Yearly);
		goal.SetStatus(GoalStatus.Paused);
		goal.SetStatus(GoalStatus.Achieved);
		goal.PausedAt.Should().BeNull();
		goal.LastResumedAt.Should().BeNull();
	}
}
