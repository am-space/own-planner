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
Before offering a choice or acknowledging a decline, the model records it through the chat-local
`task_goal_link_choice` capability. Registered declined tasks cannot be offered again in the same
conversation. New unrelated requests do not sweep existing unlinked tasks or formulate new goals.

The delegated specialist receives only explicitly authorized goal associations in its brief. Relevance,
entity references and scope do not authorize links. The parent owns suggestions and subsequent link
confirmation using the successful task creation actions, including their task IDs. Scoped delegation
continues to reject broad goal reads; the parent performs permitted goal matching after creation.

### Focused mutation

`ITaskGoalLinkService`/`TaskGoalLinkService` delegate to `ITaskItemRepository.LinkToActiveGoalAsync`
and map its explicit outcomes. The tenant-bound SQLite repository opens one write transaction and
conditionally updates only `GoalId`/`UpdatedAt`, using the Domain's shared `MonotonicClock` for the
timestamp, while checking the non-trashed, unlinked task, available list and Active goal. It reads
the successful task snapshot in that transaction before
committing. Missing/trashed tasks, missing or archived lists, inactive goals and conflicting existing
associations fail safely. Repeating the same active link returns the task without a write. A competing
link never overwrites the winner, and other task fields are not rewritten from detached snapshots. This tool does not replace
existing associations; an explicit replacement continues to use existing editing workflows in modes that permit them.

The additive shared MCP tool `taskitem_link_goal` requires only `taskId` and `goalId` and returns the
existing task DTO or `{ error }`. Cancellation propagates to Application and repositories. Both SDK
hosts (web HTTP MCP and stdio) register `TaskGoalLinkTools`, and `DirectToolMcpAdapter` discovers the
same handler. No tenant selector, new route, database migration or UI change is required.

General loads goal reads and focused linking with `task_management`; Week Planning receives them
through its existing baseline skill. Day Work adds `goal_list`, `goal_get` and `taskitem_link_goal`;
Global Planning adds focused linking and local choice recording. Day Work also receives local choice
recording. Reflection and System Analysis retain their permissions and
receive no task creation guidance. Day Work continues without general task or goal editing tools.

### Conversation lifetime and evidence

Application's `TaskGoalLinkConversationState` holds pending task/goal UUID pairs and declined task
IDs. The chat adapter owns one instance per conversation, injects its JSON data into each permitted
model request, and handles `task_goal_link_choice` locally. `action=offer` requires 1-20 tasks with at
most two goal candidates; `action=decline` identifies pending task IDs. Invalid batches fail atomically,
and offers containing a declined ID are rejected. This capability changes only conversation memory,
never planner data, and is not a public MCP server operation. General loads it with task_management;
the three applicable specialized modes include it in their existing capabilities.

State survives summary compaction, configured trimming, failed-summary fallback and provider recovery
independently of replayed history. Explicit conversation/mode reset clears it. Pending entries clear
only after a successful planner link result; failures keep them available for fresh user direction.
Separate sessions never share choices. Names can be resolved from the retained IDs using authenticated
reads. Summary instructions still preserve choice context, but state retention does not depend on the
model summary. No account-wide preferences or database persistence table are added.

Semantic relevance and recognition of natural-language consent/declines remain model instructions,
consistent with existing chat skills. Recorded choices and declines, mode permissions, tenant isolation
and atomic mutation validation are enforced in code. Scripted provider tests verify instruction
delivery, tool rounds and retained conversation context, including actual trim/failed-summary paths; they do not demonstrate live Gemini matching
quality or prove compliance with every prompt.
Application tests cover outcome mapping and cancellation. SQLite tests cover unchanged task fields,
concurrent conflicting links and changed eligibility. Integration tests run web and Telegram requests through the actual planning/session/tool/database path, including cross-user
rejection and active-only goal reads.

## Consequences

- Goal linking is available consistently in every task-creating mode and both chat channels.
- Existing contracts remain compatible; the only new public MCP operation is the focused linking tool.
- Optional matching may need a goal read plus one local choice-recording round for relevant planning
  work. Urgent and clearly unrelated capture skip this work; no goal list is added to initial context.
- Relevance and consent depend on the model following trusted instructions. Revisit with live model
  evaluations if suggestion quality or consent handling proves unreliable; do not present scripted
  responses as live model evidence.
- Conversation choices live in memory for the session lifetime. Explicit mode/session resets or
  session expiration start a new conversation. Compaction and recovery do not discard recorded IDs.
- Atomic linking serializes with other SQLite writers; other ordinary task mutations retain their
  existing repository behavior.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Chat/TaskGoalLinkGuidance.cs` | Shared creation, matching and confirmation instructions |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Guidance and mode permissions |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | General/weekly task skill capabilities |
| `OwnPlanner.Application/Tasks/TaskGoalLinkService.cs` | Application outcome mapping for atomic linking |
| `OwnPlanner.Infrastructure/Repositories/TaskItemRepository.cs` | Atomic eligibility checks, link and returned snapshot |
| `OwnPlanner.Application/Chat/TaskGoalLinkConversationState.cs` | Structured pending choices and declines |
| `OwnPlanner.Mcp.Tools/TaskGoalLinkTools.cs` | Shared additive MCP handler |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Specialist and summary instructions |
| `OwnPlanner.Infrastructure.Tests/Adapters/TaskGoalLinkConversationTests.cs` | Scripted orchestration evidence |
| `OwnPlanner.Web.Server.Tests/Services/TaskGoalLinkIntegrationTests.cs` | Web/Telegram database and tenant coverage |
