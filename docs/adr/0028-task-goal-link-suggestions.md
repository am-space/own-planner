# ADR-0028: Shared task goal-link suggestions

**Date:** 2026-10-01\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#70](https://github.com/am-space/own-planner/issues/70) asks chat to notice when newly created tasks
support an active goal. Accurate associations improve the goal-focused weekly review delivered by
[ADR-0026](0026-goal-focused-weekly-review.md). [ADR-0027](0027-paused-goals-and-focus-guidance.md)
adds paused goals, which must be excluded from suggestions. Task creation is available directly in
General, Week Planning and Day Work, and through Task Planning delegation in General and Global
Planning. Both web and Telegram use the same Application planning service and authenticated tools.

The [implementation plan](../archive/task-goal-suggestions-plan.md) maps criteria and verification.

## Decision

### Shared semantic guidance

`TaskGoalLinkGuidance` in Application supplies the same instructions to every task-creating mode.
The model compares task and active goal titles/descriptions by meaning; no keyword score, separate
matching model, new report preload or automatic linking is introduced. For an explicitly named goal,
the assistant resolves the goal and supplies `goalId` during creation without another confirmation.
Otherwise it creates tasks unlinked first, then uses fresh `goal_list includeInactive=false` for
relevant planning work. Urgent or clearly unrelated work skips optional goal reads and suggestions.

A clear match produces at most one short sentence offering a link. Multiple plausible matches produce
at most two named choices or no suggestion. Batch creation produces one combined suggestion for
successful unlinked tasks, including tasks created through plan decomposition. The user confirms a
specific task-to-goal choice before it is applied; an ambiguous yes to two alternatives needs a choice.
Declined tasks are not suggested again in the same conversation. New unrelated requests do not sweep
existing unlinked tasks or formulate new goals.

The delegated specialist receives only explicitly authorized goal associations in its brief. Relevance,
entity references and scope do not authorize links. The parent owns suggestions and subsequent link
confirmation using the successful task creation actions, including their task IDs. Scoped delegation
continues to reject broad goal reads; the parent performs permitted goal matching after creation.

### Focused mutation

`ITaskGoalLinkService`/`TaskGoalLinkService` validate the current user's task, task list and goal before
reusing the existing task update service to change only `GoalId`. Missing/trashed tasks, missing or
archived lists, inactive goals and conflicting existing associations fail safely. Repeating the same
active link returns the task without a write. This tool does not replace existing associations; an
explicit replacement continues to use existing editing workflows in modes that permit them.

The additive shared MCP tool `taskitem_link_goal` requires only `taskId` and `goalId` and returns the
existing task DTO or `{ error }`. Cancellation propagates to Application and repositories. Both SDK
hosts (web HTTP MCP and stdio) register `TaskGoalLinkTools`, and `DirectToolMcpAdapter` discovers the
same handler. No tenant selector, new route, database migration or UI change is required.

General loads goal reads and focused linking with `task_management`; Week Planning receives them
through its existing baseline skill. Day Work adds `goal_list`, `goal_get` and `taskitem_link_goal`;
Global Planning adds the linking tool. Reflection and System Analysis retain their permissions and
receive no task creation guidance. Day Work continues without general task or goal editing tools.

### Conversation lifetime and evidence

Existing conversation history holds pending choices and declines. Gemini summarization instructions
preserve task identities (titles and IDs when available), declined links and the distinction between
suggestion, consent and applied action. If compaction removes IDs, the assistant resolves targets with
existing reads instead of inventing them. There is no account-wide suppression preference or new
conversation persistence table.

Semantic relevance, natural-language consent and decline memory are model instructions, consistent
with existing chat skills. Mode permissions, tenant isolation and mutation validation are enforced in
code. Scripted provider tests verify instruction delivery, tool rounds and retained conversation
context; they do not demonstrate live Gemini matching quality or prove compliance with every prompt.
Application tests cover guarded writes and unchanged task fields. Integration tests run web and
Telegram requests through the actual planning/session/tool/database path, including cross-user
rejection and active-only goal reads.

## Consequences

- Goal linking is available consistently in every task-creating mode and both chat channels.
- Existing contracts remain compatible; the only new external surface is the focused MCP tool.
- Optional matching may need one additional goal read for relevant planning work. Urgent and clearly
  unrelated capture skip this work; no goal list is added to initial mode context.
- Relevance and consent depend on the model following trusted instructions. Revisit with live model
  evaluations if suggestion quality or consent handling proves unreliable; do not present scripted
  responses as live model evidence.
- Conversation suppression lasts as long as the corresponding history/summary remains available.
  Explicit mode/session resets start a new conversation; history trimming can discard old choices.
- Existing repository updates do not introduce a new atomic concurrency protocol. Validation checks
  current state before updating, consistent with ordinary task mutations.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Chat/TaskGoalLinkGuidance.cs` | Shared creation, matching and confirmation instructions |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Guidance and mode permissions |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | General/weekly task skill capabilities |
| `OwnPlanner.Application/Tasks/TaskGoalLinkService.cs` | Focused current-state validation and link |
| `OwnPlanner.Mcp.Tools/TaskGoalLinkTools.cs` | Shared additive MCP handler |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Specialist and summary instructions |
| `OwnPlanner.Infrastructure.Tests/Adapters/TaskGoalLinkConversationTests.cs` | Scripted orchestration evidence |
| `OwnPlanner.Web.Server.Tests/Services/TaskGoalLinkIntegrationTests.cs` | Web/Telegram database and tenant coverage |
