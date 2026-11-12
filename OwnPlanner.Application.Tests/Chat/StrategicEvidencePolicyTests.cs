using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class StrategicEvidencePolicyTests
{
	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.SystemAnalysis)]
	[InlineData(PlanningMode.General)]
	public void StrategicSkillDeliversEvidenceRulesWithinExistingModePermissions(PlanningMode mode)
	{
		var config = ModeConfig.All[mode];
		var runtime = new ChatSkillRuntime(ChatToolPolicy.ForMode(config), config.AllowedTools);
		var before = runtime.ActiveTools.ToArray();
		if (mode == PlanningMode.General) runtime.Load("strategic_review");

		runtime.Instructions.Should().Contain("does not mean no linked tasks ever")
			.And.Contain("Completed-only goals and goals with no linked tasks can have the same signal")
			.And.Contain("Do not infer staleness or inactivity duration")
			.And.Contain("existing inactive goals are not counted as missing")
			.And.Contain("everyday and operational tasks need not serve a goal")
			.And.Contain("Consider new setup or intentional emptiness")
			.And.Contain("observed fact, potential planning consequence and concrete check")
			.And.Contain("Prioritize by demonstrated planning impact")
			.And.Contain("no significant issues detected in the available data")
			.And.Contain("neither represents the full dataset or complete content")
			.And.Contain("additional evidence can materially change a conclusion")
			.And.Contain("Obtain fresh relevant evidence when current state matters")
			.And.Contain("Exploratory analysis and proposals do not authorize writes")
			.And.Contain("without redundant confirmation");
		if (mode != PlanningMode.General)
		{
			runtime.ActiveTools.Should().BeEquivalentTo(before);
			config.PreloadTools.Should().Equal("strategic_report_get");
		}
		else
			runtime.ActiveTools.Except(before).Should().OnlyContain(name => ChatSkillRegistry.ReadTools.Contains(name));
	}

	[Fact]
	public void StrategicModesRetainDistinctOutcomesAndExecutionCeilings()
	{
		var global = ModeConfig.All[PlanningMode.GlobalPlanning];
		global.CanWrite.Should().BeTrue();
		global.SkillIds.Should().Equal("goals_organization", "notes", "strategic_review");
		global.BaselineSkillIds.Should().Equal(global.SkillIds);
		global.SystemPrompt.Should().Contain("priorities and direction")
			.And.Contain("small set of concrete proposals")
			.And.Contain("Exploratory analysis and proposals do not authorize writes");
		global.AllowedTools.Should().Contain("goal_update").And.NotContain("taskitem_update");

		var analysis = ModeConfig.All[PlanningMode.SystemAnalysis];
		analysis.CanWrite.Should().BeFalse();
		analysis.SkillIds.Should().Equal("strategic_review");
		analysis.BaselineSkillIds.Should().Equal(analysis.SkillIds);
		analysis.AllowedTools.Should().BeEquivalentTo(ChatSkillRegistry.All["strategic_review"].Tools.Append("datetime_get_current"));
		analysis.AllowedTools.Should().OnlyContain(name => ChatSkillRegistry.ReadTools.Contains(name));
		analysis.SystemPrompt.Should().Contain("Do not make any changes, offer to execute fixes or switch modes automatically")
			.And.Contain("what the user can review elsewhere");
		foreach (var config in new[] { global, analysis })
			config.SystemPrompt.Should().NotContain("Orphaned goals").And.NotContain("Flag orphaned")
				.And.NotContain("Flag stale").And.NotContain("Stale contexts");
	}
}
