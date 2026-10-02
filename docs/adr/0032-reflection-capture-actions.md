# ADR-0032: Narrow capture actions in Reflection

**Date:** 2026-10-02\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

---

## Context

Reflection invited users to create tasks from unreviewed captures but could neither create tasks nor
move source notes. [Issue #76](https://github.com/am-space/own-planner/issues/76) requests exactly
those existing operations, without broad task-management permissions or an atomic conversion API.
The [implementation plan](../archive/reflection-capture-actions-plan.md) records the acceptance mapping.
This extends the narrow mode composition in [ADR-0022](0022-chat-capabilities-and-delegation.md),
retaining its skill catalog and runtime policy. No earlier ADR is superseded.

## Decision

Add `taskitem_create` and `noteitem_assign` directly to Reflection's allowed-tool composition in
Application `ModeConfig`. Both are baseline declarations on the first request; the public reflection
skill, report/calendar preload, existing retrospective notes and goal status capabilities stay intact.
Do not add `task_management`, a new skill, or broad task-writing operation groups.

Reflection instructions require explicit intent and a clear target/destination, without repeated
confirmation of clear requests. Requested task creation defaults to the system Inbox selected
through `tasklist_all includeUnassigned=true` unless the user chooses another list. Targeted note
reads supply full capture content when needed. Creating a task does not move, delete or alter its
source. Movement requires separate explicit intent and an unambiguous destination, resolved with
`notelist_all includeUnassigned=true`. List defaults exclude unassigned lists; this existing option
includes the system Inbox and legacy destinations without changing tool contracts.

When both actions are requested they execute independently. Tool results determine which action
succeeded, failed or remains uncertain. Do not claim a capture is fully processed after partial
failure or automatically retry uncertain task creation. Refresh relevant reads after changes.
General task editing, scheduling, completion, assignment, Trash/recovery/deletion, source-note
deletion and task goal-linking remain outside Reflection's permission ceiling.

Reuse the existing Application services, shared MCP handlers and tenant-bound host wiring. No new
transport shapes, persistent state, migration, dependency or transaction abstraction is introduced.

## Consequences

### Positive

- Reflection can complete clearly requested capture actions with two direct existing capabilities.
- Source notes remain intact until a separate requested move; independent results remain visible.
- Exact permission tests retain the narrow scope and prevent accidental broad task access.

### Negative / Trade-offs

- Separate operations can partially fail; the conversation must accurately report each outcome.
- Existing task creation has no conversion-specific idempotency key; uncertain results cannot be
  retried automatically without risking duplicate tasks.
- Intent interpretation and answer phrasing are model instructions. Scripted tests verify prompt
  delivery, routing, permission denials and result propagation, not live Gemini behavioral quality.

## Alternatives Considered

- Load task_management: grants unrelated edits, scheduling and task lifecycle operations.
- Add a capture conversion tool: unnecessary new contract with unresolved atomic/idempotent semantics.
- Extend the public reflection skill: broadens that skill's shared catalog responsibility; explicit
  mode declarations are sufficient for this narrow mode workflow.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Narrow declarations and capture instructions |
| `OwnPlanner.Application.Tests/Chat/ReflectionCapturePolicyTests.cs` | Immediate activation and independent retained denials |
| `OwnPlanner.Infrastructure.Tests/Adapters/ReflectionCaptureConversationTests.cs` | Scripted authorized, exploratory, ambiguous and partial-result evidence |
| `OwnPlanner.Web.Server.Tests/Services/ReflectionCaptureIntegrationTests.cs` | Real shared handlers, source preservation and cross-user isolation |
| `docs/ai-integration.md` | Living capture workflow contract |
