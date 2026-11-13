# ADR-0038: Linked recurrence successors with guarded preview and apply

**Date:** 2026-11-13\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#91](https://github.com/am-space/own-planner/issues/91), stage 3 of
[#84](https://github.com/am-space/own-planner/issues/84), requires schedule changes and resumption
without rewriting completed work or discarding old obligations. Stages 1–2 supplied immutable
calendar rules and tenant-bound lifecycle transactions ([ADR-0036](0036-recurrence-calendar-foundations.md),
[ADR-0037](0037-recurrence-persistence-lifecycle.md)). The overall
[implementation plan](../recurring-tasks-plan.md) remains active through stage 8.

## Decision

`TaskItem.CreateRecurrenceSuccessor` creates one new recurring task, copies title/description/list/
goal/importance, and links both original task IDs. DueAt and manual FocusAt are omitted. It bounds
the predecessor before the new start without extending an earlier end or changing the old rule,
anchor or missed policy. `RecurrenceSchedule.ShortenBefore` permits an explicitly empty retired
interval; public construction of a new schedule still rejects backwards bounds. Closed parents
stay closed, open parents await their existing lifecycle closure conditions. Original occurrence
facts never move, including completion/cancellation facts outside shortened generation bounds.

Application's `RecurringTaskSuccessors` exposes internal `PreviewAsync` and `ApplyAsync` through
`IRecurringTaskSuccessors`. Preview reads a consistent parent/list/fact snapshot without saving or
reconciling. It shows affected facts of all statuses from the requested start, proposed successor
schedules and corresponding predecessor bounds. Overlap requires an explicit choice to move the
start after every affected date, or keep the requested start as separate work. Moving later reanchors
the new interval and is unavailable if no representable date within the proposed end remains.
Neither choice invents completion or cancellation for the successor.

The preview token is a deterministic SHA-256 hash of the parent revision/schedule/copied metadata,
request, proposed schedule and ordered affected facts. Apply recalculates it inside the same
non-deferred SQLite transaction that saves the cutoff, both linked tasks and the successful request.
Stale choices require a fresh preview. The token validates state; tenant authorization still comes
from the existing user-bound factory and current task/list access checks.

`RecurringSuccessorOperations` records a tenant-local GUID request key, request fingerprint and
original result IDs. Matching retries return the first IDs even after subsequent chain changes;
reuse with different request/token/choice fails. Unique source/successor indexes and a single
successor per chain tip prevent siblings. Nullable soft task links retain historical identities
without deletion cascades between series. The ledger has cascading foreign keys to both tasks;
removing either participating task removes the associated retry record.

Replace requires an open parent; Resume requires a completed parent. Optional rule/policy inherit,
and end inherits unless explicitly cleared. Resume drops an inherited end before the chosen start;
an explicit backwards end is rejected. Both use the selected date as a new anchor. An inactive gap
creates no obligations/skips because old parents remain closed. Ordinary reopen remains unchanged;
recurring reopen requires the explicit successor/start operation. Historical correction alone never
resumes or reopens a parent.

The CLI-generated `AddRecurringSuccessors` migration changes only AppDbContext. Web and stdio
register the shared service; no HTTP/MCP schema, chat permission or frontend is introduced.

## Consequences

### Positive

- Schedule history, first completion timestamps and old retained obligations survive changes.
- Database transactions enforce atomicity and retries across independent processes/connections.
- Preview remains read-only, and conflict resolution is rechecked against fact writes as well as parent writes.
- Existing ordinary tasks, tenant resolution and external contracts remain compatible.

### Negative / Trade-offs

- A changed parent revision, even a reconciliation checkpoint, can require another preview.
- Conflict preview loads all affected facts; it does not silently truncate the decision evidence.
- Soft links can point to a permanently removed series; retry records disappear with either task.
- DateOnly.MinValue cannot be a successor start, since the preceding cutoff is unrepresentable.
- New-series creation cannot use a retired empty schedule; only predecessor shortening permits that shape.

## Deferred

History/unified reads (#92), reports (#93), Weekly Review (#94), public MCP/chat (#95), UI and final
integration (#96). Public recurring-task creation remains disabled until those safeguards are ready.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Domain/Tasks/TaskItem.cs` | Linked successor creation and common-field copying |
| `OwnPlanner.Domain/Tasks/RecurrenceSchedule.cs` | End-only shortening and explicit empty retired ranges |
| `OwnPlanner.Application/Tasks/Recurrence/RecurringTaskSuccessors.cs` | Preview/choice/revalidation/retry policy |
| `OwnPlanner.Application/Tasks/Recurrence/IRecurringTaskStore.cs` | Tenant-bound snapshot and atomic write ports |
| `OwnPlanner.Infrastructure/Tasks/RecurringTaskStore.cs` | Consistent SQLite snapshots, links and request persistence |
| `OwnPlanner.Infrastructure/Persistence/AppDbContext.cs` | Unique links, ledger and cleanup mappings |
| `OwnPlanner.Infrastructure.Tests/Tasks/RecurringTaskSuccessorTests.cs` | Conflict, retry, race, rollback, resumption and migration tests |
