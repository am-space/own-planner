namespace OwnPlanner.Application.Chat;

/// <summary>Shared conversation rules for direct and delegated task creation in every writing mode.</summary>
public static class TaskGoalLinkGuidance
{
	public const string Instructions = """
		When creating tasks, follow these goal-link rules, including tasks from a larger plan:
		- If the user explicitly names a goal for the task in the original request, resolve that exact goal with goal_list/goal_get and supply goalId to taskitem_create immediately, without another confirmation. Ask only if the named goal is ambiguous.
		- Otherwise create the requested tasks without goalId first. For relevant planning work, retrieve current active goals with goal_list includeInactive=false using the permitted tools. Never delay task capture for optional matching; skip suggestions and optional goal lookups for urgent or clearly unrelated work. In General load task_management for direct task creation, matching or a later link confirmation.
		- Compare the task title/description and user context with active goal titles/descriptions by meaning, not just shared words. For example, booking physiotherapy may support a half-marathon goal. Consider only Active goals, never Paused, Achieved or Dropped goals. If no goal clearly matches, make no goal-related suggestion or question. Do not formulate new goals.
		- For exactly one clear match, confirm creation and append at most one short sentence offering to link the task to that named goal. If several goals plausibly match, offer at most the two most likely goals or omit the suggestion; never show the full goal list or guess a link.
		- For several successfully created tasks, make one combined suggestion for the batch, not a question per task. Mention only the relevant task-to-goal choices; if these cannot fit one short sentence, omit the optional suggestion. Do not suggest links for failed creations, already-linked tasks or unrelated work, and do not sweep existing unlinked tasks.
		- A suggestion is not permission to link. Keep tasks unlinked until the user confirms the specific task-to-goal choice, then call taskitem_link_goal with the confirmed taskId and goalId. If a reply to two alternatives is merely 'yes', ask which goal. If IDs were lost during compaction, resolve the pending task and goal with targeted reads and ask when ambiguous; never invent IDs or infer a new choice. Report only successful links; on a stale/inactive/conflicting target explain the failure and ask for direction instead of silently substituting another goal.
		- If the user declines, remember the declined tasks in this conversation and never suggest linking those same tasks again, including after refresh or history compaction. Do not call a linking tool on a decline. A later explicit user request may authorize a link. Keep pending choices and declined task identities in conversation context; do not expose IDs in ordinary replies.
		- When delegating Task Planning, pass only goal links explicitly authorized by the user in the brief. Creating tasks for a plan does not authorize inferred links. After delegation the parent applies these same suggestion rules to successfully created unlinked tasks in the returned actions, combining the entire batch. Resolve and apply a confirmed link in the parent; do not delegate the user's short confirmation without the pending choice.
		""";

	public const string SummaryInstructions = "Preserve pending task-to-goal link choices and declined task identities (titles and IDs when available), so a declined task is not suggested again in the same conversation. Preserve whether a link was merely suggested, explicitly confirmed, or successfully applied; never turn a suggestion into authorization.";
}
