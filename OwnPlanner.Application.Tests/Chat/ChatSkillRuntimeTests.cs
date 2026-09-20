using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class ChatSkillRuntimeTests
{
	private static ChatToolPolicy General => ChatToolPolicy.ForMode(ModeConfig.All[PlanningMode.General]);
	private static ChatSkillRuntime Create(ChatToolPolicy? policy = null) => new(policy ?? General, General.AllowedTools);

	[Fact]
	public void Catalog_HasSixResponsibilitiesAndRecoveryBelongsToTaskManagement()
	{
		ChatSkillRegistry.All.Keys.Should().BeEquivalentTo("task_management", "notes", "goals_organization", "weekly_planning", "reflection", "strategic_review");
		ChatSkillRegistry.All["weekly_planning"].Tools.Should().NotContain(["taskitem_restore", "taskitem_reopen", "taskitem_list_trash"]);
		ChatSkillRegistry.All["goals_organization"].Tools.Should().NotContain("strategic_report_get");
		ChatSkillRegistry.All["strategic_review"].Tools.Should().OnlyContain(tool => ChatSkillRegistry.ReadTools.Contains(tool));
		var runtime = Create();
		runtime.Load("task_management");
		runtime.ActiveTools.Should().Contain(["taskitem_create", "taskitem_update", "taskitem_assign", "taskitem_set_focus_date", "taskitem_set_important", "taskitem_complete", "taskitem_reopen", "taskitem_delete", "taskitem_list_trash", "taskitem_restore"]);
		runtime.ActiveTools.Should().NotContain("taskitem_delete_permanently");
	}

	[Fact]
	public void MissingBaselineSkillTools_PreserveInstalledToolsWithoutAdvertisingIncompleteSkill()
	{
		var policy = ChatToolPolicy.ForMode(ModeConfig.All[PlanningMode.Reflection]);
		var runtime = new ChatSkillRuntime(policy, ["datetime_get_current", "goal_get"]);
		runtime.ActiveTools.Should().BeEquivalentTo("datetime_get_current", "goal_get");
		runtime.Instructions.Should().BeEmpty();
		var load = () => runtime.Load("reflection");
		load.Should().Throw<InvalidOperationException>().WithMessage("*required planner tools*");
	}

	[Fact]
	public void General_StartsCompactWithDirectAgentsAndDiscoverableSkills()
	{
		var runtime = Create();
		runtime.ActiveTools.Should().BeEquivalentTo("general_report_get", "datetime_get_current", "skill_load", "task_planning_agent_call", "search_agent_call");
		foreach (var skill in ChatSkillRegistry.All.Values)
		{
			runtime.Catalog.Should().Contain(skill.Id).And.Contain(skill.Description);
			runtime.Instructions.Should().NotContain(skill.Instructions);
		}
		General.AllowedTools.Should().NotContain(["context_delete", "tasklist_delete", "notelist_delete", "goal_delete", "noteitem_delete"]);
	}

	[Fact]
	public void Load_DeduplicatesInstructionsAndOverlappingTools_AndNewRequestsReset()
	{
		var runtime = Create();
		runtime.Load("notes");
		runtime.Load("notes");
		runtime.Load("reflection");
		runtime.Instructions.Split(ChatSkillRegistry.All["notes"].Instructions).Should().HaveCount(2);
		runtime.ActiveTools.Should().OnlyHaveUniqueItems().And.Contain("noteitem_get");
		Create().Instructions.Should().BeEmpty();
		Create().ActiveTools.Should().NotContain("noteitem_get");
	}

	[Fact]
	public void Load_RequiresTheNextModelRoundBeforeNewToolsCanExecute()
	{
		var runtime = Create();
		var declarations = runtime.ActiveTools;
		runtime.Load("notes");
		var denied = () => runtime.EnsureCanExecute("noteitem_create", declarations);
		denied.Should().Throw<InvalidOperationException>().WithMessage("*next round*");
		runtime.EnsureCanExecute("noteitem_create", runtime.ActiveTools);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("unknown")]
	public void Load_UnknownSkillDoesNotChangeState(string? id)
	{
		var runtime = Create();
		var act = () => runtime.Load(id);
		act.Should().Throw<InvalidOperationException>().WithMessage("Unknown skill*");
		runtime.ActiveTools.Should().BeEquivalentTo(General.BaselineTools);
		runtime.Instructions.Should().BeEmpty();
	}

	[Fact]
	public void Load_MissingToolFailsAtomicallyWithoutPartialActivation()
	{
		var runtime = new ChatSkillRuntime(General, General.AllowedTools.Except(["noteitem_get"]));
		var act = () => runtime.Load("notes");
		act.Should().Throw<InvalidOperationException>().WithMessage("*required planner tools*");
		runtime.ActiveTools.Should().BeEquivalentTo(General.BaselineTools);
		runtime.Instructions.Should().BeEmpty();
	}

	[Fact]
	public void ReadOnlyPolicy_DeniesWriteSkillsAndWriteAgentsEvenIfAccidentallyAllowlisted()
	{
		var runtime = Create(General with { CanWrite = false });
		runtime.Catalog.Should().NotContain("- notes:");
		runtime.ActiveTools.Should().NotContain("task_planning_agent_call");
		var load = () => runtime.Load("notes");
		load.Should().Throw<InvalidOperationException>().WithMessage("*not permitted*");
		var call = () => runtime.EnsureCanExecute("task_planning_agent_call", General.AllowedTools.ToHashSet());
		call.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void PermissionCeilingAndSkillAllowlist_AreBothRequired()
	{
		var restricted = Create(General with { AllowedTools = General.AllowedTools.Except(["noteitem_create"]).ToArray() });
		var disallowedSkill = Create(General with { SkillIds = [] });
		foreach (var runtime in new[] { restricted, disallowedSkill })
		{
			var act = () => runtime.Load("notes");
			act.Should().Throw<InvalidOperationException>().WithMessage("*not permitted*");
			runtime.Catalog.Should().NotContain("- notes:");
		}
	}

	[Fact]
	public void BaselineSkills_AreReappliedToEachRequest()
	{
		var policy = General with { BaselineSkillIds = ["notes"] };
		var first = Create(policy);
		first.Load("weekly_planning");
		var next = Create(policy);
		next.ActiveTools.Should().Contain("noteitem_get").And.NotContain("weekly_report_get");
		next.Instructions.Should().Contain(ChatSkillRegistry.All["notes"].Instructions);
	}

	[Fact]
	public void ExistingModeValuesAndPermissions_ArePreserved()
	{
		((int)PlanningMode.GlobalPlanning).Should().Be(0);
		((int)PlanningMode.SystemAnalysis).Should().Be(4);
		((int)PlanningMode.General).Should().Be(5);
		foreach (var config in ModeConfig.All.Values.Where(config => config.ModeId != PlanningMode.General))
		{
			var policy = ChatToolPolicy.ForMode(config);
			var runtime = new ChatSkillRuntime(policy, config.AllowedTools);
			runtime.ActiveTools.Should().BeEquivalentTo(config.AllowedTools);
			runtime.Catalog.Should().BeEmpty();
		}
	}
}
