using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public class ModeConfigTests
{
	[Fact]
	public void TaskPlanningAgent_IsExposedOnlyInGlobalPlanning()
	{
		ModeConfig.All[PlanningMode.GlobalPlanning].AllowedTools.Should().Contain("task_planning_agent_call");
		ModeConfig.All.Where(pair => pair.Key != PlanningMode.GlobalPlanning)
			.Should().OnlyContain(pair => !pair.Value.AllowedTools.Contains("task_planning_agent_call"));
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
		// An empty allow-list disables filtering (every tool is exposed), which defeats the per-mode
		// scoping. Each mode must declare the tools it needs.
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
		config.AllowedTools.Should().Contain(config.PreloadTools);
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
