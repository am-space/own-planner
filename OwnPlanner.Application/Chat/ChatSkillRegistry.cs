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
		new("task_management", "Retrieve and edit tasks; complete, reopen, move to Trash and restore.",
			"Use targeted task and list lookups to identify the user's target. For simple clearly authorized edits, call task tools directly. Ask when the target or intended change is ambiguous; exploratory ideas do not authorize writes. taskitem_delete moves to recoverable Trash; inspect Trash with taskitem_list_trash and restore with taskitem_restore. Reopen completed tasks with taskitem_reopen. To remove a deadline, use taskitem_update with clearDueAt=true; null dueAt leaves it unchanged. Clearing a deadline preserves the focus date. Confirm deadline removal only when the tool result shows dueAt=null. Never permanently delete tasks. Report only confirmed changes and refresh targeted data when needed. Where the mode exposes Task Planning delegation, reserve it for multi-step decomposition and planning; use proposal behavior for exploration and execution for authorized changes without an extra confirmation step.",
			ChatCapabilities.Combine(ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead, ChatCapabilities.TaskEdit, ChatCapabilities.TaskProgress, ChatCapabilities.TaskRecovery,
				["taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_delete"])),
		new("notes", "Capture, retrieve and organize notes.",
			"Retrieve only the notes needed for the request. Discuss exploratory ideas before writing. Create, edit, assign or pin notes when requested, and report the changes actually completed. Loading this skill does not fetch note bodies.",
			ChatCapabilities.Combine(ChatCapabilities.NoteListRead, ChatCapabilities.NoteListWrite, ChatCapabilities.NoteListArchive,
				ChatCapabilities.NoteRead, ChatCapabilities.NoteWrite, ChatCapabilities.NoteOrganize, ["noteitem_list_by_goal"])),
		new("goals_organization", "Manage goals, contexts and task/note list structure.",
			"Use targeted lookups to organize goals, contexts and lists only in response to user intent. For alignment and structural diagnosis use strategic_review. For task lifecycle operations use task_management where available. This skill supplies no destructive context or list removal tools.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.GoalWrite, ChatCapabilities.ContextRead, ChatCapabilities.ContextWrite,
				ChatCapabilities.TaskListRead, ChatCapabilities.TaskListWrite, ChatCapabilities.TaskListArchive,
				ChatCapabilities.NoteListRead, ChatCapabilities.NoteListWrite, ChatCapabilities.NoteListArchive)),
		new("weekly_planning", "Review workload, prioritize and schedule the coming week.",
			"For the shared local-calendar carryover workflow, read weekly_review_settings_get, then weekly_review_open. When responding to a reminder, use its targetWeek date to resume that exact period, including deferrals. If timezone is missing ask the user to select it; never guess UTC. Opt in only on explicit request through weekly_review_configure, preserving existing preferences when disabling. Review manageable pages; never treat a bounded sample as the entire backlog. Propose focus dates in the target week considering commitments. For authorized review changes prefer weekly_review_apply with the revision from a fresh review page; handle applied=false without blindly retrying. Before other authorized mutations retrieve taskitem_get and its list; skip completed, trashed, archived or changed targets and explain any partial failures. Offer keeping, resetting or removing overdue deadlines; use clearDueAt=true only when requested. Rescheduling or clearing focus never changes deadlines. Opening/accepting review does not authorize mutations. Explicit directions such as move all six authorize exactly those identified tasks without redundant confirmation. Refresh the review after changes. weekly_review_transition supports explicit completion, skipping and deferral; resolve tomorrow to a definite local time in the frozen review timezone, asking when ambiguous. Task completion alone never finishes the review. After addressing a suitable planning request call weekly_review_offer and append only its returned invitation; omit this during urgent or unrelated work and never switch modes. The older weekly_report_get still uses its existing UTC window. Keep flexible focus dates separate from due-date commitments. Discuss priorities and capacity before speculative scheduling; apply changes when authorized. Task lifecycle rules belong to task_management. Use only the task operations available in this mode. Refresh targeted data after changes; prior workflow context does not make a dated report current. Do not repeatedly append full reports.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead,
				["weekly_report_get", "weekly_review_settings_get", "weekly_review_configure", "weekly_review_open", "weekly_review_transition", "weekly_review_offer", "weekly_review_apply", "taskitem_list_by_goal", "taskitem_list_by_focus_date"])),
		new("reflection", "Review completion evidence, carryover, captures and retrospectives.",
			"Use the reflection report when relevant. Explain its current-state and historical limitations; never invent past completion transitions or goal states. Retrieve relevant captures as needed. Write a retrospective note or update goal status only when requested. Suggest task changes without implying unavailable tools can execute them. Refresh targeted evidence when freshness matters.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.NoteListRead, ChatCapabilities.NoteRead, ChatCapabilities.NoteWrite,
				ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead,
				["reflection_report_get", "goal_update", "noteitem_list_by_goal", "taskitem_list_by_goal"])),
		new("strategic_review", "Read-only alignment review and structural diagnosis.",
			"Use the strategic report and targeted reads for goal alignment, orphaned goals, stale contexts and list structure. This skill supplies only read tools. Diagnose and propose improvements; applying requested changes requires separately permitted write capabilities. Respect read-only modes. A preloaded report is a dated snapshot; retrieve fresh targeted evidence when necessary and respect report limitations.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.ContextRead, ChatCapabilities.TaskListRead,
				ChatCapabilities.TaskRead, ChatCapabilities.NoteListRead, ChatCapabilities.NoteRead,
				["strategic_report_get", "taskitem_list_by_focus_date"]))
	}.ToFrozenDictionary(skill => skill.Id, StringComparer.Ordinal);

	// Explicit rather than inferred from tool names: unknown capabilities fail closed in read-only modes.
	public static IReadOnlySet<string> ReadTools { get; } = new[]
	{
		"goal_list", "goal_get", "context_list", "context_get", "tasklist_all", "tasklist_get",
		"notelist_all", "notelist_get", "noteitem_list_items", "noteitem_list_by_goal", "noteitem_get",
		"taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get",
		"taskitem_list_trash", "strategic_report_get", "weekly_report_get", "reflection_report_get",
		"general_report_get", "weekly_review_settings_get", "datetime_get_current", "search_agent_call", LoadToolName
	}.ToFrozenSet(StringComparer.Ordinal);
}
