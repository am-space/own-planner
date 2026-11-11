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
			"Use targeted task and list lookups to identify the user's target. For simple clearly authorized edits, call task tools directly. Ask when the target or intended change is ambiguous; exploratory ideas do not authorize writes. taskitem_delete moves to recoverable Trash; inspect Trash with taskitem_list_trash and restore with taskitem_restore. Reopen completed tasks with taskitem_reopen. To remove a deadline, use taskitem_update with clearDueAt=true; null dueAt leaves it unchanged. Clearing a deadline preserves the focus date. Confirm deadline removal only when the tool result shows dueAt=null. Never permanently delete tasks. Report only confirmed changes and refresh targeted data when needed. Where the mode exposes Task Planning delegation, reserve it for multi-step decomposition and planning; use proposal behavior for exploration and execution for authorized changes without an extra confirmation step. Follow the shared task creation goal-link rules, using goal_list and taskitem_link_goal for relevant suggestions and confirmed links.",
			ChatCapabilities.Combine(ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead, ChatCapabilities.TaskEdit, ChatCapabilities.TaskProgress, ChatCapabilities.TaskRecovery, ChatCapabilities.GoalRead,
				[TaskGoalLinkConversationState.ToolName, "taskitem_link_goal", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_delete"])),
		new("notes", "Capture, retrieve and organize notes.",
			"Retrieve only the notes needed for the request. Discuss exploratory ideas before writing. Create, edit, assign or pin notes when requested, and report the changes actually completed. Loading this skill does not fetch note bodies.",
			ChatCapabilities.Combine(ChatCapabilities.NoteListRead, ChatCapabilities.NoteListWrite, ChatCapabilities.NoteListArchive,
				ChatCapabilities.NoteRead, ChatCapabilities.NoteWrite, ChatCapabilities.NoteOrganize, ["noteitem_list_by_goal"])),
		new("goals_organization", "Manage goals, contexts and task/note list structure.",
			"Use targeted lookups to organize goals, contexts and lists only in response to user intent. Pause an active goal with goal_update status=Paused and resume with status=Active only on request. To show paused goals use goal_list includeInactive=true and select Paused entries. Append returned activeGoalWarning once after a successful creation or resume; the limit is advisory. For alignment and structural diagnosis use strategic_review. For task lifecycle operations use task_management where available. This skill supplies no destructive context or list removal tools.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.GoalWrite, ChatCapabilities.ContextRead, ChatCapabilities.ContextWrite,
				ChatCapabilities.TaskListRead, ChatCapabilities.TaskListWrite, ChatCapabilities.TaskListArchive,
				ChatCapabilities.NoteListRead, ChatCapabilities.NoteListWrite, ChatCapabilities.NoteListArchive)),
		new("weekly_planning", "Review workload, prioritize and schedule the coming week.",
			"""
			Interpret IsOverloaded and overloadedDays as task-count concentration signals to review, not proof of overload or feasibility.
			They flag days whose distinct focus-or-due task count is at least overloadedDayThreshold (default 5); preserve that numerical meaning.
			Focus plans and due commitments are separate views: a task focused and due on the same day counts once in its distinct total.
			Do not add the two counts as if they were independent tasks, or assume a due date means all work happens that day.
			Task counts do not measure duration or available time. Five short tasks and five large tasks can require very different effort.
			For questions such as "What's realistic to get done this week?", use capacity, task sizes and constraints already supplied in the conversation.
			When essential information is missing, ask briefly about available time and the largest tasks; do not repeat answered questions or a full intake questionnaire.
			State when duration or available time is unknown; never invent estimates, free time or precise capacity conclusions from task counts or bounded samples.
			When useful, propose a small core set of tasks and optional reserve work, explaining choices through actual deadlines, importance, goals and user constraints.
			A capacity discussion or proposed plan does not authorize writes. Execute concrete requested changes without redundant confirmation; clarify ambiguous targets or intent.
			Use FocusAt through taskitem_set_focus_date (or weekly_review_apply focus/goalFocus during review) to distribute flexible planned work across days.
			Create or change DueAt only for genuine deadlines supported by user intent; never manufacture deadlines to fill a schedule.
			Scheduling work without a real deadline changes only its focus date and preserves any existing deadline.
			""" + "\n\n" +
			"For the shared local-calendar weekly review, read weekly_review_settings_get, then weekly_review_open. At the beginning append activeGoalWarning if returned, once. Include pausedGoalsMention in one line if returned and offer to resume; do not reconstruct monthly mentions on refresh. Paused goals are excluded from active goal review and flags. Review every active goal first, putting flagged goals first. Explain completed work, remaining tasks, computed flags and optional metric progress; ask what the user will do for each goal next week. Plan an existing linked task with weekly_review_apply goalFocus, or create a linked task and set its focus date only after the user chooses it. If the user wants no plan for a goal, confirm that once, then move on. For stalled or passed goals, ask whether to keep, pause, achieve, drop or move the target period; use goal_update only for explicit choices. If suggestCreatingGoals is true, offer once to formulate 1-3 goals; declining continues to tasks. When responding to a reminder, use its targetWeek date to resume that exact period, including deferrals. If timezone is missing ask the user to select it; never guess UTC. Opt in only on explicit request through weekly_review_configure, preserving existing preferences when disabling. After goals, review manageable task pages; tasks already planned for goals are omitted. Suggest linking an unlinked task to one clearly matching active goal only when relevant; use weekly_review_apply linkGoal only after confirmation. Never treat a bounded sample as the entire backlog. Propose focus dates in the target week considering commitments. For authorized review changes prefer weekly_review_apply with the revision from a fresh review page; handle applied=false without blindly retrying. Before other authorized mutations retrieve taskitem_get and its list; skip completed, trashed, archived or changed targets and explain any partial failures. Offer keeping, resetting or removing overdue deadlines; use clearDueAt=true only when requested. Rescheduling or clearing focus never changes deadlines. Opening/accepting review does not authorize mutations. Explicit directions such as move all six authorize exactly those identified tasks without redundant confirmation. Refresh the review after changes and summarize completed and planned goal work versus other work, naming goals still without a target-week plan. For a request solely to skip, defer or finish without presenting the review, obtain its identity with weekly_review_open present=false so guidance remains available for an actual opening. weekly_review_transition supports explicit completion, skipping and deferral; resolve tomorrow to a definite local time in the frozen review timezone, asking when ambiguous. Task completion alone never finishes the review. After addressing a suitable planning request call weekly_review_offer and append only its returned invitation; omit this during urgent or unrelated work and never switch modes. For ordinary calendar planning use weekly_report_get calendarPeriod=thisWeek, nextWeek or nextSevenDays as requested; its omitted calendarPeriod retains the legacy UTC window. Ordinary reports never open a review and thisWeek never targets next week on the last day. Keep flexible focus dates separate from due-date commitments. Discuss priorities and capacity before speculative scheduling; apply changes when authorized. Task lifecycle rules belong to task_management. Use only the task operations available in this mode. Refresh targeted data after changes; prior workflow context does not make a dated report current. Do not repeatedly append full reports.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.GoalWrite, ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead,
				["weekly_report_get", "weekly_review_settings_get", "weekly_review_configure", "weekly_review_open", "weekly_review_transition", "weekly_review_offer", "weekly_review_apply", "taskitem_list_by_goal", "taskitem_list_by_focus_date"])),
		new("reflection", "Review completion evidence, carryover, captures and retrospectives.",
			"Use the reflection report when relevant. Explain its current-state and historical limitations; never invent past completion transitions or goal states. Retrieve relevant captures as needed. Write a retrospective note or update goal status only when requested. Suggest task changes without implying unavailable tools can execute them. Refresh targeted evidence when freshness matters.",
			ChatCapabilities.Combine(ChatCapabilities.GoalRead, ChatCapabilities.NoteListRead, ChatCapabilities.NoteRead, ChatCapabilities.NoteWrite,
				ChatCapabilities.TaskListRead, ChatCapabilities.TaskRead,
				["reflection_report_get", "goal_update", "noteitem_list_by_goal", "taskitem_list_by_goal"])),
		new("strategic_review", "Read-only alignment review and structural diagnosis.",
			"""
			Use the strategic report and targeted reads to review goal alignment and planning structure from evidence.
			ActiveGoalsWithoutActiveTasks means active goals with no incomplete, non-trashed linked tasks in non-archived task lists; it does not mean no linked tasks ever.
			Completed-only goals and goals with no linked tasks can have the same signal. Check relevant task/goal details when this distinction matters.
			Use permitted task list reads with includeCompleted=true when completed work matters; follow hasMore before treating a page as exhaustive.
			Consider a next step, completing or pausing a goal only as possibilities supported by context, not automatic corrections.
			ContextsWithoutActiveTasks means non-archived contexts with no such incomplete tasks; notes and completed work may still exist.
			Do not infer staleness or inactivity duration from this snapshot.
			TasksWithoutGoal covers eligible incomplete tasks with a null or missing goal reference; links to existing inactive goals are not counted as missing.
			Missing goal links are candidates for review, not mandatory errors: everyday and operational tasks need not serve a goal.
			ContextsWithoutTaskOrNoteLists means no non-archived task or note lists. Consider new setup or intentional emptiness before proposing cleanup.
			For each substantive finding, connect the observed fact, potential planning consequence and concrete check or proposed next step. Keep benign observations concise.
			Prioritize by demonstrated planning impact, not missing links or empty structures alone. Label unknown consequences as requiring review, not automatically urgent.
			Do not manufacture findings; "no significant issues detected in the available data" is a valid result.
			Task/note samples are bounded and previews may be truncated; neither represents the full dataset or complete content. Counts describe only the report's selected scope.
			Use targeted get/list reads when additional evidence can materially change a conclusion; retrieve full content when a truncated preview is insufficient.
			A preloaded report is a dated snapshot. Obtain fresh relevant evidence when current state matters; do not infer historical activity or past transitions.
			This skill supplies only read tools. Exploratory analysis and proposals do not authorize writes. Execute clearly requested changes only through separately permitted write capabilities, without redundant confirmation; clarify ambiguous targets or intent.
			Respect read-only modes: System Analysis must not offer to execute fixes or switch modes automatically. Recommendations may describe what the user can review elsewhere.
			""",
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
		"calendar_period_get", "general_report_get", "general_attention_get", "day_report_get", "weekly_review_settings_get", "datetime_get_current", "search_agent_call", LoadToolName
	}.ToFrozenSet(StringComparer.Ordinal);
}
