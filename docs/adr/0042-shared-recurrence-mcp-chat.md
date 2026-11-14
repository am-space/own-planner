# ADR-0042: Shared recurrence MCP operations and scoped chat behavior

**Date:** 2026-11-14\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#95](https://github.com/am-space/own-planner/issues/95) activates stage 7 of
[#84](https://github.com/am-space/own-planner/issues/84) after occurrence-aware reports and Weekly
Review (ADR-0040/0041). Calendar, lifecycle, successors and history already exist. Public operations
must not turn one occurrence completion into series termination, invent completion times, transfer
completed work to successor schedules, or widen tenant/delegated scope. The overall
[implementation plan](../archive/recurring-tasks-plan.md) stays active until stage 8.

## Decision

Use the Application `IRecurringTaskManagement` facade for one-parent creation, atomic ordinary-task
configuration, details and successor rule intent mapping. It reuses existing repositories, the
recurring transaction store and successor service. Immutable schedules can only be replaced by a
linked successor. Required explicit missed policy is validated before insertion. Weekly creation
captures shared calendar alignment; changed Weekly successor rules require explicit weekStart from
calendar metadata, preserving identical preview/apply/retry inputs even after preference edits.

Expose eleven additive `RecurringTaskTools` handlers: recurring_task_create/configure/get/end,
planning_work_query, task_occurrence_get/history/complete/cancel and
recurring_task_successor_preview/apply. Parsing is strict for calendar dates and offset-bearing actual
instants. Domain/Application retain business transitions, actual timestamp validation, idempotency,
expiry, history selection and conflict revalidation. Reads never reconcile. Successor application
requires an explicit user conflict choice when affected facts exist; no completion moves to a new
identity. Resume creates a successor, keeping its predecessor closed and preserving the break.

Register the same handlers in direct chat execution, bearer-only HTTP MCP and stdio, with shared
host-bound DI. Console uses stdio. Generate argument schemas with the SDK for all new tools, including
direct discovery, rather than duplicating nullable/default schema logic. Normalize new results once
with shared MCP serializer options before transport delivery, preserving named enums, omitted nulls,
DateOnly strings and UTC completion timestamps identically. Existing task/report tool contracts
retain their original schemas/serialization. Ordinary completion/reopen semantics stay compatible;
descriptions distinguish whole-series ending, and recurring reopen returns a safe directed error.

Compose recurrence reads with existing task-read capabilities. General obtains management through
its request-scoped task_management skill; Week Planning has management directly. Day Work has
occurrence completion/cancellation, whole-series ending and reads/preview, without schedule apply or
creation. Reflection retains its narrow capture/goal/note writes, and System Analysis remains read-only.
Global Planning can read directly and delegate authorized execution. Delegated proposals offer reads
and successor preview only. Execution checks each targeted source/destination list/context; mixed
queries force scope filters, and scoped history requires an in-scope task. All persistence remains
bound to the authenticated host session, with no tenant/path arguments.

Attach shared recurrence guidance to mode prompts and delegated instructions: explicit missed policy,
composite identities, actual versus scheduled dates, first completion timestamp, full-period expiry,
series-ending intent, explicit conflict choices, preserved history and refreshed evidence after writes.
Creation/configuration are not automatically retried after uncertain results. Scripted provider tests
verify orchestration and permission enforcement, separately from any real-model evaluation.

## Consequences

- Every transport uses the same Application behavior and the same new tool argument/result contracts.
- Existing list tools keep one stored row per series; projection and history supply occurrence detail.
- No schema/migration, dependency or frontend change is needed for this stage.
- Creation remains a single configured-parent insert and is not request-key idempotent. A concurrent
  list archive can race the initial eligibility read, as with existing ordinary creation. Configuration
  and existing lifecycle/successor writes validate eligibility under their tenant write transaction.
- Delegated source scope checks retain the existing check-before-forward boundary; concurrent list
  reassignment is not a frozen delegation snapshot. Scoped query filters are applied in the shared
  read snapshot. Stronger atomic delegated mutation scoping would need a separate Application contract.
- Shared read models still load current source metadata and selected facts; bounded payloads and lazy
  generated obligation streams do not make source metadata memory use constant.
- Real-model evaluation is not substituted by scripted conversations. Final series UI and broader
  readiness remain #96; this stage does not authorize a release or deployment.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Tasks/Recurrence/RecurringTaskManagement.cs` | Shared management facade and intent validation |
| `OwnPlanner.Mcp.Tools/RecurringTaskTools.cs` | Additive schemas, parsing, canonical results and thin handlers |
| `OwnPlanner.Application/Chat/RecurrenceGuidance.cs` | Primary/delegated chat safety and freshness |
| `OwnPlanner.Application/Chat/TaskPlanningAgentContracts.cs` | Delegated permissions and list/context scope |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/DirectToolMcpAdapter.cs` | Direct registration and shared SDK schema generation |
| `OwnPlanner.E2E.Tests/McpTransportE2eTests.Recurrence.cs` | Real transport flows, schemas, history and tenant isolation |
