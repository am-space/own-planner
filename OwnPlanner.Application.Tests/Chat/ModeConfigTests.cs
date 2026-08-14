using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public class ModeConfigTests
{
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
}
