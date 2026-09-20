using FluentAssertions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public sealed class ModePermissionSnapshotTests
{
	// Frozen from merged d704568 before #59. Do not derive expected permissions from production groups.
	private static readonly Dictionary<PlanningMode, string[]> Expected = new()
	{
		[PlanningMode.GlobalPlanning] = [ "goal_list", "goal_get", "goal_create", "goal_update", "goal_delete", "context_list", "context_get", "context_create", "context_update", "context_delete", "tasklist_all", "tasklist_get", "tasklist_create", "tasklist_update", "tasklist_archive", "tasklist_unarchive", "tasklist_delete", "notelist_all", "notelist_get", "notelist_create", "notelist_update", "notelist_archive", "notelist_unarchive", "notelist_delete", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update", "noteitem_assign", "noteitem_pin", "noteitem_unpin", "noteitem_delete", "taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get", "taskitem_list_trash", "taskitem_restore", "strategic_report_get", "datetime_get_current", "search_agent_call", "task_planning_agent_call" ],
		[PlanningMode.WeekPlanning] = [ "goal_list", "goal_get", "tasklist_all", "tasklist_get", "tasklist_create", "tasklist_update", "tasklist_archive", "tasklist_unarchive", "tasklist_delete", "taskitem_list_items", "taskitem_list_by_focus_date", "taskitem_list_by_goal", "taskitem_get", "taskitem_create", "taskitem_update", "taskitem_assign", "taskitem_set_focus_date", "taskitem_set_important", "taskitem_complete", "taskitem_reopen", "taskitem_delete", "taskitem_list_trash", "taskitem_restore", "weekly_report_get", "datetime_get_current", "search_agent_call" ],
		[PlanningMode.DayWork] = [ "taskitem_list_by_focus_date", "taskitem_list_items", "taskitem_get", "taskitem_create", "taskitem_complete", "taskitem_reopen", "taskitem_set_focus_date", "taskitem_set_important", "tasklist_all", "tasklist_get", "notelist_all", "noteitem_create", "datetime_get_current" ],
		[PlanningMode.Reflection] = [ "goal_list", "goal_get", "goal_update", "notelist_all", "notelist_get", "notelist_create", "notelist_update", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update", "tasklist_all", "tasklist_get", "taskitem_list_items", "taskitem_list_by_goal", "taskitem_get", "reflection_report_get", "datetime_get_current", "search_agent_call" ],
		[PlanningMode.SystemAnalysis] = [ "goal_list", "goal_get", "context_list", "context_get", "tasklist_all", "tasklist_get", "taskitem_list_items", "taskitem_list_by_focus_date", "taskitem_get", "notelist_all", "notelist_get", "noteitem_list_items", "noteitem_get", "strategic_report_get", "datetime_get_current" ],
	};

	[Fact]
	public void SpecializedModes_PreserveDirectAndDelegatedPermissions()
	{
		foreach (var (mode, expected) in Expected)
		{
			var config = ModeConfig.All[mode];
			config.AllowedTools.Should().BeEquivalentTo(expected, because: mode.ToString());
			var runtime = new ChatSkillRuntime(ChatToolPolicy.ForMode(config), expected);
			runtime.ActiveTools.Should().BeEquivalentTo(expected);
		}
		TaskPlanningMcpAdapter.WriteTools.Should().BeEquivalentTo("tasklist_create", "tasklist_update", "taskitem_create", "taskitem_update", "taskitem_assign", "taskitem_set_focus_date", "taskitem_set_important", "taskitem_complete", "taskitem_delete");
	}
}
