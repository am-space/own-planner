using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public class ModeConfigTests
{
	[Fact]
	public void TaskPlanningAgent_IsExposedOnlyInGlobalPlanningAndGeneral()
	{
		ModeConfig.All[PlanningMode.GlobalPlanning].AllowedTools.Should().Contain("task_planning_agent_call");
		ModeConfig.All[PlanningMode.General].InitialTools.Should().Contain("task_planning_agent_call");
		ModeConfig.All.Where(pair => pair.Key is not PlanningMode.GlobalPlanning and not PlanningMode.General)
			.Should().OnlyContain(pair => !pair.Value.AllowedTools.Contains("task_planning_agent_call"));
	}

	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.WeekPlanning)]
	public void TrashManagementTools_AreExposedInPlanningModesThatManageTaskStructure(PlanningMode mode)
	{
		ModeConfig.All[mode].AllowedTools.Should().Contain(["taskitem_list_trash", "taskitem_restore"]);
	}

	public static TheoryData<PlanningMode> AllModes()
	{
		var data = new TheoryData<PlanningMode>();
		foreach (var mode in ModeConfig.All.Keys)
			data.Add(mode);
		return data;
	}

	[Theory]
	[MemberData(nameof(AllModes))]
	public void AllowedTools_IsNonEmpty(PlanningMode mode)
	{
		// Each supported planning mode declares its intended capabilities explicitly.
		ModeConfig.All[mode].AllowedTools.Should().NotBeEmpty();
	}

	[Theory]
	[MemberData(nameof(AllModes))]
	public void AllowedTools_HasNoDuplicates(PlanningMode mode)
	{
		var allowed = ModeConfig.All[mode].AllowedTools;
		allowed.Should().OnlyHaveUniqueItems();
	}

	[Theory]
	[MemberData(nameof(AllModes))]
	public void PreloadTools_AreAllInAllowedTools(PlanningMode mode)
	{
		// The model must be able to re-call whatever it was preloaded with (e.g. to refresh state),
		// so every preload tool has to be in the allow-list.
		var config = ModeConfig.All[mode];
		config.PreloadTools.Should().OnlyContain(tool => config.AllowedTools.Contains(tool));
	}

	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public void StrategicModes_PreloadOnlyStrategicReport(PlanningMode mode)
	{
		ModeConfig.All[mode].PreloadTools.Should().Equal("strategic_report_get");
	}

	[Fact]
	public void WeekPlanning_PreloadsOnlyWeeklyReport()
	{
		var config = ModeConfig.All[PlanningMode.WeekPlanning];

		config.PreloadTools.Should().Equal("weekly_report_get");
		config.AllowedTools.Should().Contain("weekly_report_get");
	}

	[Fact]
	public void Reflection_PreloadsOnlyReflectionReport()
	{
		var config = ModeConfig.All[PlanningMode.Reflection];

		config.PreloadTools.Should().Equal("reflection_report_get");
		config.AllowedTools.Should().Contain("reflection_report_get");
	}
}
