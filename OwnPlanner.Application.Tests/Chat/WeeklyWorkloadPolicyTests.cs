using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class WeeklyWorkloadPolicyTests
{
	[Theory]
	[InlineData(PlanningMode.WeekPlanning)]
	[InlineData(PlanningMode.General)]
	public void SharedWeeklySkillSuppliesEvidenceAndAuthorizationRules(PlanningMode mode)
	{
		var config = ModeConfig.All[mode];
		var runtime = new ChatSkillRuntime(ChatToolPolicy.ForMode(config), config.AllowedTools);
		if (mode == PlanningMode.General) runtime.Load("weekly_planning");

		runtime.Instructions.Should().Contain("not proof of overload or feasibility")
			.And.Contain("at least overloadedDayThreshold (default 5)")
			.And.Contain("same day counts once")
			.And.Contain("constraints already supplied in the conversation")
			.And.Contain("ask briefly about available time and the largest tasks")
			.And.Contain("never invent estimates, free time or precise capacity conclusions")
			.And.Contain("small core set of tasks and optional reserve work")
			.And.Contain("actual deadlines, importance, goals and user constraints")
			.And.Contain("A capacity discussion or proposed plan does not authorize writes")
			.And.Contain("Execute concrete requested changes without redundant confirmation")
			.And.Contain("never manufacture deadlines to fill a schedule")
			.And.Contain("preserves any existing deadline");
	}

	[Fact]
	public void WeekPlanningRetainsSharedCalendarAndReviewWhileRemovingUnsupportedRecommendations()
	{
		var config = ModeConfig.All[PlanningMode.WeekPlanning];
		config.SystemPrompt.Should().NotContain("Suggest due date assignments")
			.And.NotContain("tasks that won't realistically get done")
			.And.Contain("Use FocusAt for planned work")
			.And.Contain("Exploratory capacity questions do not authorize writes");
		config.PreloadTools.Should().Equal("weekly_report_get");
		config.PreloadCalendarPeriod.Should().Be("thisWeek");
		config.SkillIds.Should().Equal("weekly_planning", "task_management");
		config.BaselineSkillIds.Should().Equal("weekly_planning", "task_management");
		ChatSkillRegistry.All["weekly_planning"].Instructions.Should()
			.Contain("Opening/accepting review does not authorize mutations")
			.And.Contain("weekly_review_open present=false")
			.And.Contain("Ordinary reports never open a review")
			.And.Contain("calendarPeriod=thisWeek, nextWeek or nextSevenDays");
	}
}
