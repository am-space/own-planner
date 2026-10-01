using FluentAssertions;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.Tests.WeeklyReviews;

public sealed class WeeklyReviewGoalCalculatorTests
{
	private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
	private static readonly WeeklyReviewState Review = new() { TimeZoneId = "UTC", TargetWeek = new DateOnly(2026, 9, 21) };

	[Fact]
	public void OrdersAttentionFirstAndCalculatesFlagsAndWorkShare()
	{
		var healthy = Row("Healthy", Now.AddDays(-30), GoalHorizon.Yearly, "2026", open: 2, completed14: 1, planned: 1);
		var stalled = Row("Stalled", Now.AddDays(-30), GoalHorizon.Quarterly, "2026-Q3", open: 1);
		var fresh = Row("Fresh", Now.AddDays(-13), GoalHorizon.Monthly, "2026-09", open: 0);
		var data = new WeeklyReviewGoalData([healthy, stalled, fresh], 2, 3, 1, 4);

		var result = WeeklyReviewGoalCalculator.Build(data, Review, Now);

		result.Items.Select(goal => goal.Title).Should().Equal("Fresh", "Stalled", "Healthy");
		result.Items.Single(goal => goal.Title == "Fresh").Stalled.Should().BeFalse();
		result.Items.Single(goal => goal.Title == "Fresh").NoNextStep.Should().BeTrue();
		result.Items.Single(goal => goal.Title == "Stalled").Stalled.Should().BeTrue();
		result.Items.Single(goal => goal.Title == "Stalled").TargetPeriodFlag.Should().Be("targetPeriodEndingSoon");
		result.Items.Single(goal => goal.Title == "Healthy").TargetPeriodFlag.Should().BeNull();
		result.UnplannedGoalIds.Should().BeEquivalentTo([stalled.Id, fresh.Id]);
		result.CompletedGoalWorkCount.Should().Be(2);
		result.CompletedOtherWorkCount.Should().Be(3);
		result.PlannedGoalWorkCount.Should().Be(1);
		result.PlannedOtherWorkCount.Should().Be(4);
	}

	[Theory]
	[InlineData(GoalHorizon.Monthly, "2026-08", "targetPeriodPassed")]
	[InlineData(GoalHorizon.Quarterly, "2026-Q3", "targetPeriodEndingSoon")]
	[InlineData(GoalHorizon.Yearly, "2026", null)]
	[InlineData(GoalHorizon.Monthly, "September 2026", null)]
	[InlineData(GoalHorizon.Quarterly, "2026-Q5", null)]
	public void ParsesOnlySupportedTargetPeriods(GoalHorizon horizon, string period, string? expected)
	{
		var goal = Row("Goal", Now.AddDays(-30), horizon, period, open: 1, completed14: 1);
		WeeklyReviewGoalCalculator.Build(new([goal], 0, 0, 0, 0), Review, Now)
			.Items.Single().TargetPeriodFlag.Should().Be(expected);
	}

	[Fact]
	public void TargetDateUsesStoredCalendarDate()
	{
		var goal = Row("Date", Now.AddDays(-30), GoalHorizon.TargetDate, null, open: 1, completed14: 1) with
		{
			TargetDate = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc)
		};
		WeeklyReviewGoalCalculator.Build(new([goal], 0, 0, 0, 0), Review, Now)
			.Items.Single().TargetPeriodFlag.Should().Be("targetPeriodPassed");
	}

	[Theory]
	[InlineData(13, false)]
	[InlineData(14, true)]
	[InlineData(15, true)]
	public void ResumedGoalHasFourteenDayStalledGrace(int daysSinceResume, bool stalled)
	{
		var row = Row("Resumed", Now.AddDays(-90), GoalHorizon.Yearly, "2026", open: 1) with { LastResumedAt = Now.AddDays(-daysSinceResume) };
		WeeklyReviewGoalCalculator.Build(new([row], 0, 0, 0, 0), Review, Now).Items.Single().Stalled.Should().Be(stalled);
	}

	private static WeeklyReviewGoalRow Row(string title, DateTime created, GoalHorizon horizon, string? period,
		int open, int completed14 = 0, int planned = 0) =>
		new(Guid.NewGuid(), title, created, horizon, period, null, null, null, 0, [], completed14, open, [], planned);
}
