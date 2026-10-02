using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class ReflectionCapturePolicyTests
{
	[Fact]
	public void TwoCaptureActionsAreImmediatelyActiveWithoutChangingSkillsOrPreload()
	{
		var config = ModeConfig.All[PlanningMode.Reflection];
		var policy = ChatToolPolicy.ForMode(config);
		var runtime = new ChatSkillRuntime(policy, config.AllowedTools);
		config.PreloadTools.Should().Equal("reflection_report_get");
		config.PreloadCalendarPeriod.Should().Be("lastWeek");
		config.SkillIds.Should().Equal("reflection");
		config.BaselineSkillIds.Should().Equal("reflection");
		config.InitialTools.Should().BeNull();
		runtime.ActiveTools.Should().Contain("taskitem_create", "noteitem_assign", "goal_update", "noteitem_create");
		runtime.ActiveTools.Should().NotContain("skill_load");
		runtime.EnsureCanExecute("taskitem_create", runtime.ActiveTools);
		runtime.EnsureCanExecute("noteitem_assign", runtime.ActiveTools);
		ChatSkillRegistry.All["reflection"].Tools.Should().NotContain("taskitem_create", "noteitem_assign");
		runtime.Instructions.Should().Contain(ChatSkillRegistry.All["reflection"].Instructions);
	}

	[Fact]
	public void BroaderTaskAndDestructiveNoteActionsRemainDeniedEvenIfHostInstallsThem()
	{
		string[] denied = ["taskitem_update", "taskitem_set_focus_date", "taskitem_set_important", "taskitem_complete", "taskitem_reopen",
			"taskitem_assign", "taskitem_delete", "taskitem_list_trash", "taskitem_restore", "taskitem_delete_permanently", "taskitem_link_goal",
			"noteitem_delete", "task_planning_agent_call", "skill_load"];
		var config = ModeConfig.All[PlanningMode.Reflection];
		var runtime = new ChatSkillRuntime(ChatToolPolicy.ForMode(config), config.AllowedTools.Concat(denied));
		foreach (var tool in denied)
		{
			runtime.ActiveTools.Should().NotContain(tool);
			// Simulate a stale/malicious declaration too; the permission ceiling still rejects it.
			var execute = () => runtime.EnsureCanExecute(tool, config.AllowedTools.Concat(denied).ToHashSet());
			execute.Should().Throw<InvalidOperationException>();
		}
		var load = () => runtime.Load("task_management");
		load.Should().Throw<InvalidOperationException>().WithMessage("*not permitted*");
	}
}
