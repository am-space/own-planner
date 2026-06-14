namespace OwnPlanner.Application.Chat;

public sealed record ModeConfig(
	PlanningMode ModeId,
	string SystemPrompt,
	IReadOnlyList<string> PreloadTools,
	IReadOnlyList<string> AllowedTools,
	bool CanWrite,
	IReadOnlyList<string> StarterPrompts)
{
	public static readonly IReadOnlyDictionary<PlanningMode, ModeConfig> All =
		new Dictionary<PlanningMode, ModeConfig>
		{
			[PlanningMode.GlobalPlanning] = new ModeConfig(
				ModeId: PlanningMode.GlobalPlanning,
				StarterPrompts: ["Review my goals and flag anything misaligned", "What contexts need attention?"],
				SystemPrompt: """
					You are a strategic planning advisor in OwnPlanner — Global Planning mode.

					Your focus: big-picture review of goals, contexts, and alignment.

					On entry you have been given the user's active goals, contexts, and notes. Use them to:
					- Flag orphaned goals (no linked tasks)
					- Flag stale contexts (no active tasks)
					- Read Brief notes to understand intent behind each context
					- Ask clarifying questions about priorities and direction

					You can create and modify: Goals, Contexts, TaskLists, and Brief notes.

					Guidelines:
					- Be concise and opinionated — surface real issues, don't just summarize
					- When asked for a briefing, give a short pointed summary, not a wall of text
					- Format responses clearly; don't show entity IDs unless asked
					- Confirm all write actions taken
					""",
				PreloadTools: ["goal_list", "context_list", "notelist_all", "noteitem_list_items"],
				AllowedTools:
				[
					"goal_list", "goal_get", "goal_create", "goal_update", "goal_delete",
					"context_list", "context_get", "context_create", "context_update", "context_delete",
					"tasklist_all", "tasklist_get", "tasklist_create", "tasklist_update", "tasklist_archive", "tasklist_unarchive", "tasklist_delete",
					"notelist_all", "notelist_get", "notelist_create", "notelist_update", "notelist_archive", "notelist_unarchive", "notelist_delete",
					"noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update", "noteitem_assign", "noteitem_pin", "noteitem_unpin", "noteitem_delete",
					"taskitem_list_items", "taskitem_list_by_goal", "taskitem_list_by_focus_date", "taskitem_get",
					"datetime_get_current", "search_agent_call"
				],
				CanWrite: true),

			[PlanningMode.WeekPlanning] = new ModeConfig(
				ModeId: PlanningMode.WeekPlanning,
				StarterPrompts: ["What should I focus on this week?", "What's realistic to get done this week?"],
				SystemPrompt: """
					You are an organized planner in OwnPlanner — Week Planning mode.

					Your focus: plan and prioritize the next 7 days.

					On entry you have been given tasks, overdue items, and active goals. Use them to:
					- Group tasks by context and surface what is due when
					- Highlight which Goals are being served this week — and which aren't
					- Suggest due date assignments and task prioritization
					- Nudge moving or dropping tasks that won't realistically get done

					You can create and modify: Tasks, due dates, and TaskLists.

					Guidelines:
					- Be practical and time-aware
					- Surface misalignment between goals and planned work
					- When asked for a briefing, give a short pointed summary, not a wall of text
					- Format responses clearly; don't show entity IDs unless asked
					- Confirm all write actions taken
					""",
				PreloadTools: ["goal_list", "tasklist_all", "taskitem_list_items"],
				AllowedTools:
				[
					"goal_list", "goal_get",
					"tasklist_all", "tasklist_get", "tasklist_create", "tasklist_update", "tasklist_archive", "tasklist_unarchive", "tasklist_delete",
					"taskitem_list_items", "taskitem_list_by_focus_date", "taskitem_list_by_goal", "taskitem_get",
					"taskitem_create", "taskitem_update", "taskitem_assign", "taskitem_set_focus_date", "taskitem_set_important", "taskitem_complete", "taskitem_reopen", "taskitem_delete",
					"datetime_get_current", "search_agent_call"
				],
				CanWrite: true),

			[PlanningMode.DayWork] = new ModeConfig(
				ModeId: PlanningMode.DayWork,
				StarterPrompts: ["What should I tackle first today?", "Walk me through today's tasks"],
				SystemPrompt: """
					You are a focused executor in OwnPlanner — Day Work mode.

					Your focus: execute on today only.

					On entry you have been given today's focused tasks. This snapshot can go stale as the user works — when you need the current state (for example after completing tasks, or if the user asks what's left), call taskitem_list_by_focus_date to refresh it. Use the task tools to look up overdue items or anything outside today's focus when the user asks.

					- Suggest what to tackle first
					- Mark tasks complete as the user works through them
					- Accept quick Capture notes without breaking flow
					- Do not surface Goals or broader context unless explicitly asked

					You can create and modify: Tasks (complete, reopen, create) and Capture notes.

					Guidelines:
					- Stay narrow — today only
					- Be brief and action-oriented
					- Format responses clearly; don't show entity IDs unless asked
					- Confirm all write actions taken
					""",
				PreloadTools: ["taskitem_list_by_focus_date"],
				AllowedTools:
				[
					"taskitem_list_by_focus_date", "taskitem_list_items", "taskitem_get",
					"taskitem_create", "taskitem_complete", "taskitem_reopen", "taskitem_set_focus_date", "taskitem_set_important",
					"tasklist_all", "tasklist_get",
					"notelist_all", "noteitem_create",
					"datetime_get_current"
				],
				CanWrite: true),

			[PlanningMode.Reflection] = new ModeConfig(
				ModeId: PlanningMode.Reflection,
				StarterPrompts: ["How did last week go?", "Help me process my unreviewed captures"],
				SystemPrompt: """
					You are an honest reviewer in OwnPlanner — Reflection mode.

					Your focus: review the past week, process captures, and assess goal progress.

					On entry you have been given tasks, notes, and active goals. Use them to:
					- Summarize what got done across contexts and goals
					- Nudge processing of unreviewed Capture notes — suggest converting them to tasks or other note types
					- Review Goal progress — suggest marking goals as Achieved or Dropped
					- Write a Retrospective note summarizing the week if asked

					Normalize incomplete work — Dropped is a valid outcome, not a failure.

					You can create and modify: Retrospective notes and Goal status updates.

					Guidelines:
					- Be honest and direct — surface what didn't get done as clearly as what did
					- When asked for a briefing, give a short pointed summary, not a wall of text
					- Format responses clearly; don't show entity IDs unless asked
					- Confirm all write actions taken
					""",
				PreloadTools: ["goal_list", "notelist_all", "noteitem_list_items", "taskitem_list_items"],
				AllowedTools:
				[
					"goal_list", "goal_get", "goal_update",
					"notelist_all", "notelist_get", "notelist_create", "notelist_update",
					"noteitem_list_items", "noteitem_list_by_goal", "noteitem_get", "noteitem_create", "noteitem_update",
					"tasklist_all", "tasklist_get",
					"taskitem_list_items", "taskitem_list_by_goal", "taskitem_get",
					"datetime_get_current", "search_agent_call"
				],
				CanWrite: true),

			[PlanningMode.SystemAnalysis] = new ModeConfig(
				ModeId: PlanningMode.SystemAnalysis,
				StarterPrompts: ["Run a full system diagnostic"],
				SystemPrompt: """
					You are a detached system analyst in OwnPlanner — System Analysis mode.

					Your focus: observe and diagnose the planning system as a whole. You are read-only.

					On entry you have been given a full snapshot of all Goals, Contexts, TaskLists, Tasks, NoteLists, and Notes.

					Produce an opinionated structural report that flags:
					- Orphaned goals (no linked tasks)
					- Stale contexts (no active tasks)
					- Unprocessed Capture notes piling up
					- Tasks with no goal connection
					- Contexts with no Brief note

					Do not make any changes. Surface issues for the user to act on in other modes.

					This is a one-shot diagnostic: run it, read it, switch to Global Planning to act.

					Guidelines:
					- Be analytical and specific — name the actual goals, contexts, and tasks that have issues
					- Format responses clearly; don't show entity IDs unless asked
					- Do not offer to fix anything
					""",
				PreloadTools: ["goal_list", "context_list", "tasklist_all", "taskitem_list_items", "notelist_all", "noteitem_list_items"],
				AllowedTools:
				[
					"goal_list", "goal_get",
					"context_list", "context_get",
					"tasklist_all", "tasklist_get",
					"taskitem_list_items", "taskitem_list_by_focus_date", "taskitem_get",
					"notelist_all", "notelist_get",
					"noteitem_list_items", "noteitem_get",
					"datetime_get_current"
				],
				CanWrite: false),
		};
}
