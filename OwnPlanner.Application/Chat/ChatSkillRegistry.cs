using System.Collections.Frozen;

namespace OwnPlanner.Application.Chat;

/// <summary>A server-curated instruction set referencing existing planner tools.</summary>
public sealed record ChatSkill(string Id, string Description, string Instructions, IReadOnlyList<string> Tools);

/// <summary>Trusted skills; no user-provided code, tenant identifiers, or automatic data retrieval.</summary>
public static class ChatSkillRegistry
{
	public const string LoadToolName = "skill_load";

	public static IReadOnlyDictionary<string, ChatSkill> All { get; } = new ChatSkill[]
	{
		new("notes", "Capture, retrieve and organize notes.",
			"Retrieve only the notes needed for the request. Discuss exploratory ideas before writing. Create, edit, assign or pin notes when requested, and report the changes actually completed. Loading this skill does not fetch note bodies.",
			Array.AsReadOnly(new[] { "notelist_all", "notelist_get", "notelist_create", "notelist_update", "notelist_archive", "notelist_unarchive", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update", "noteitem_assign", "noteitem_pin", "noteitem_unpin" })),
		new("goals_organization", "Manage goals, contexts and task/note lists.",
			"Review direction and organization with targeted lookups. Change goals, contexts or lists only in response to user intent. Use the directly available Task Planning Agent for task mutations. Destructive context and list removal is unavailable.",
			Array.AsReadOnly(new[] { "goal_list", "goal_get", "goal_create", "goal_update", "context_list", "context_get", "context_create", "context_update", "tasklist_all", "tasklist_get", "tasklist_create", "tasklist_update", "tasklist_archive", "tasklist_unarchive", "notelist_all", "notelist_get", "notelist_create", "notelist_update", "notelist_archive", "notelist_unarchive", "strategic_report_get" })),
		new("weekly_planning", "Review workload and plan the next seven days; find and restore tasks.",
			"Fetch the weekly report only when relevant. Keep flexible focus dates separate from due-date commitments and respect the report's UTC window. Use targeted task lookups and the directly available Task Planning Agent for supported mutations, including explicitly requested completion or recoverable Trash. Use taskitem_restore for an explicitly requested restore, and taskitem_reopen for reopening. Refresh targeted data after changes; do not repeatedly append full reports. Never permanently delete tasks.",
			Array.AsReadOnly(new[] { "weekly_report_get", "goal_list", "goal_get", "tasklist_all", "tasklist_get", "taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get", "taskitem_list_trash", "taskitem_restore", "taskitem_reopen" })),
		new("reflection", "Review current completion evidence, carryover and Inbox captures.",
			"Fetch the reflection report explicitly. Explain its current-state and historical limitations; never invent past completion transitions or goal states. Retrieve relevant captures as needed. Write a retrospective note or update goal status only when the user requests it. Use the directly available Task Planning Agent for supported task mutations.",
			Array.AsReadOnly(new[] { "reflection_report_get", "goal_list", "goal_get", "goal_update", "notelist_all", "notelist_get", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update", "tasklist_all", "tasklist_get", "taskitem_list_items", "taskitem_list_by_goal", "taskitem_get" }))
	}.ToFrozenDictionary(skill => skill.Id, StringComparer.Ordinal);

	// Explicit rather than inferred from tool names: unknown capabilities fail closed in read-only modes.
	public static IReadOnlySet<string> ReadTools { get; } = new[]
	{
		"goal_list", "goal_get", "context_list", "context_get", "tasklist_all", "tasklist_get",
		"notelist_all", "notelist_get", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get",
		"taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get",
		"taskitem_list_trash", "strategic_report_get", "weekly_report_get", "reflection_report_get",
		"datetime_get_current", "search_agent_call", LoadToolName
	}.ToFrozenSet(StringComparer.Ordinal);
}
