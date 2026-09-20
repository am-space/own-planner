# ADR-0022: Shared chat capabilities and bounded proposal delegation

**Date:** 2026-09-20

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#59](https://github.com/am-space/own-planner/issues/59) follows merged #58 (`d704568`). General
delegated routine task writes, while the weekly skill owned recovery. Specialized modes duplicated
tool lists and the specialist received only a free-text objective. The [implementation plan](../archive/chat-capabilities-delegation-plan.md)
records the acceptance mapping. This replaces the catalog/composition decision in
[ADR-0020](0020-request-scoped-chat-skills.md), retaining its request-local activation and execution
guards. It extends the delegation design in [ADR-0011](0011-bounded-delegated-task-planning-agent.md)
and preserves the delegated lifecycle boundaries in [ADR-0016](0016-bounded-delegated-task-lifecycle-mutations.md).
General's default selection and initial report in [ADR-0021](0021-general-default-chat.md) remain intact.

## Decision

Application owns small shared operation groups, six public skills and mode composition. The catalog
is `task_management`, `notes`, `goals_organization`, `weekly_planning`, `reflection`, and
`strategic_review`. Task management owns task lifecycle guidance; weekly planning owns scheduling
and workload; strategic review supplies read-only diagnosis independently of organization writes.
General loads task management for simple authorized changes, using shared MCP handlers directly.
Ambiguous targets invite questions; exploratory ideas invite discussion or a proposal. Delegation
remains useful for multi-step planning and execution, without reconfirming clearly authorized work.

The specialized permission matrix is frozen in a regression test from merged `d704568`. There are
no intentional changes to those effective permissions, including Global Planning's delegation
capability. Global, Week, Reflection and System Analysis use permitted public baseline skills;
Day Work composes narrow groups to avoid gaining full task management. `ChatToolPolicy` and
`ChatSkillRuntime` remain the policy boundary. Loads validate every tool, never fetch data, and
only authorize newly declared tools on a later model round. Dynamic state expires after each user
request, including failure, cancellation, recovery, compaction and mode switches. Explicit baseline
instructions reapply per request. Missing host tools suppress incomplete baseline skill instructions,
while installed baseline tools remain usable; explicit incomplete loads still fail atomically.

The chat-local delegation schema adds optional `behavior` and `brief`. Omitted behavior means
`execution`, preserving old calls; new routing guidance explicitly selects `proposal` for exploration.
Invalid values fail closed. Proposal mode removes writes from the specialist declarations and denies
them again in the existing `TaskPlanningMcpAdapter` execution guard. Scope validation, tenant-bound
adapter resolution, fresh sessions, round budgets, cancellation and no recursive/cross-agent calls
remain intact. Search Agent continues external factual research without planner tools.

The specialist receives bounded JSON containing objective, behavior, optional context/list scopes,
and selected constraints, typed entity references and user decisions. It receives no parent history.
Objective: 2,000 UTF-16 code units; constraints and decisions: eight entries each, 300 units per entry;
references: 16 entries, UUID IDs, four known kinds and labels up to 120 units. Validation occurs before
scope reads; oversize input is rejected rather than losing constraints by truncation. References
never expand scope. These are explicit stricter input bounds on the chat-local schema; public
HTTP/MCP schemas and persisted mode identifiers are unchanged. No migration is needed.

Results add `proposedPlan`, at most 20 steps with bounded descriptions and optional task/list IDs.
Suggestions are never automatically executed and model output cannot populate factual `actions`.
Completed proposals return `proposed`; malformed structured proposal output returns `invalid_proposal`
and no accepted steps. Existing execution statuses remain compatible. Prompts govern conversational
intent; policy mechanically governs capabilities, proposal writes and scope.

## Verification and measurements

`ModePermissionSnapshotTests` freezes all specialized direct permissions and the delegated write
allowlist. Application tests cover catalog boundaries, atomic loads, read-only policy, request-local
activation and brief validation. Scripted SDK request tests cover direct lifecycle operations,
ambiguous targets, exploration, weekly/reflection/diagnostic workflows, research, scoped proposals,
attempted specialist mutations/recursion, malformed input/output and recovery. Existing cancellation,
round-budget, compaction/rebuild, execution-action and usage tests remain in force. Two-user web
tests exercise loaded task skills and both proposal/execution scope resolution. Telegram retains
the shared factory and tenant adapter; console uses the same chat orchestration with `McpAdapter`.
A separate local smoke run used a real stdio process and temporary SQLite database: five General
requests updated/completed/reopened/trashed/restored a task, then a scoped proposal rejected a
specialist create attempt and returned a structured plan. Temporary data and processes were cleaned up.

The scripted measurement fixture ran before production changes on `d704568` and after implementation,
using the same fake response patterns: three consecutive rename turns or three planning turns with
two mutation rounds each. Counts include parent and specialist model requests; initial-report preload
is excluded. Eleven elapsed-time samples followed one warmup. Values measure local SDK/orchestration
overhead with synthetic responses, not network latency, live Gemini reasoning, or routing quality.

| Three-turn scenario | Version | Model requests | Skill loads | Agent calls | Median elapsed (range), ms |
|---|---|---:|---:|---:|---:|
| Routine edit | Before | 12 | 0 | 3 | 9.268 (8.901–22.284) |
| Routine edit | After | 9 | 3 | 0 | 10.934 (9.430–24.124) |
| Complex planning | Before | 15 | 0 | 3 | 10.505 (9.017–31.203) |
| Complex planning | After | 15 | 0 | 3 | 7.406 (7.186–10.260) |

Routine edits eliminate three nested sessions and three model requests in this script, but reload
the skill on every turn. Observed timings vary and do not establish a latency improvement. Keep
activation request-local; persistent workflow presets are not justified by these measurements.
No live Gemini test was run because no API key was supplied for this task.

## Consequences

- Shared groups reduce duplicated permissions while exact snapshots guard against accidental grants.
- Direct task edits avoid unnecessary delegation; proposals provide a mechanically read-only path.
- Brief selection still depends on the parent model choosing relevant context. Bounds reject rather
  than repair oversized briefs, so callers may need to reformulate them.
- Omitted behavior retains write-capable execution for compatibility. Natural-language authorization
  and appropriate routing remain prompt/model responsibilities; proposal mode must be selected explicitly.
- Baseline instructions add request metadata in specialized modes. Repeated General skill loads
  remain visible overhead, with no persistent permission state.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Chat/ChatCapabilities.cs` | Shared fine-grained operation groups |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | Six public skill definitions |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Mode purposes, composition and baselines |
| `OwnPlanner.Application/Chat/ChatToolPolicy.cs` | Request-local activation and policy enforcement |
| `OwnPlanner.Application/Chat/TaskPlanningBrief.cs` | Bounded brief, behavior and validation |
| `OwnPlanner.Application/Chat/TaskPlanningAgentContracts.cs` | Scoped execution and proposal write guard |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Additive chat-local schema and fresh sessions |
| `OwnPlanner.Infrastructure/Adapters/TaskPlanningAgentOrchestrator.cs` | Structured specialist context and results |
