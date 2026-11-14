# ADR-0037: Per-user recurrence facts and explicit lifecycle reconciliation

**Date:** 2026-11-12\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#90](https://github.com/am-space/own-planner/issues/90), stage 2 of
[#84](https://github.com/am-space/own-planner/issues/84), needs persisted series and occurrence
facts without enabling public creation before reports/review are ready. It builds on
[ADR-0036](0036-recurrence-calendar-foundations.md). The overall
[implementation plan](../archive/recurring-tasks-plan.md) stays active through stage 8.

## Decision

TaskItem gains nullable immutable recurrence configuration and operational checkpoint/calendar-key/
revision fields. Infrastructure maps the schedule through an explicit JSON storage shape with ISO
DateOnly values. The CLI-generated `AddRecurringTaskLifecycle` AppDbContext migration adds these
columns and `TaskOccurrenceFacts`; ordinary fields and central AuthDbContext are unchanged.

Facts use composite primary key `(TaskId, OccurrenceDate)` with completed/skipped/cancelled status.
A database constraint requires an actual completion timestamp exactly for completed facts; UTC
round-trip ISO storage preserves the instant independently of the scheduled date. Pending dates
have no row. Fact deletion cascades from the task; Trash preserves history through the existing
nullable active-list relationship. Account export/erasure continue to operate on the whole user DB.

Application's `RecurringTaskLifecycle` owns eligibility, missed-policy, correction, retry and closure
rules. Its `IRecurringTaskStore` boundary executes callbacks under Infrastructure's non-deferred
SQLite write transaction. Current parent/list state, fact transition and returned fact share that
transaction. Completion preserves the first successful timestamp; default time comes from one
shared-calendar clock snapshot. Explicit historical time must be UTC and no later than that snapshot.
Only recorded skips can be corrected to completed; this also works on closed parents and historical
dates outside current generation bounds, without reopening or transferring work. Conflicting
completed/cancelled transitions fail. User actions exclude Trash and unavailable lists.

Explicit reconciliation scans up to 256 prefix dates per atomic transaction, with at most a final
current-period closure check. Skip policy
records missing dates before the current full calendar week/month and advances an exclusive prefix;
retain policy stores no pending rows and checks its resolved prefix only once the end passes.
Every batch commits cursor and facts together; remaining batches continue without a lookback cap.
A timezone/week-start key change rescans from the original start, preserving resolved facts.
Bounded series close only after the end passes and every eligible date has a terminal fact.
Unbounded series never auto-close; empty bounds need no fabricated fact. Parent closure leaves
CompletedAt null and contributes no occurrence completion.

The web background worker runs every minute for active central accounts under trusted existing
tenant scopes. It repeats reconciliation independently of per-process initialization, Telegram
links and disabled reminders, isolating failures per account without logging data or exception text.
The stdio worker runs after startup migration and every minute for its fixed tenant; Console uses
stdio. Lifecycle commands also catch up explicitly. No read invokes reconciliation.

Stored-task completion/Trash delegate recurring parents to shared termination/Trash with catch-up
before stopping; Trash remains available in archived lists. Ordinary behavior is
unchanged. Recurring reopen requires a successor with an explicit start (stage 3). Trash ends a
series and restore keeps it closed so inactive gaps cannot become obligations. Archiving a list
hides work without suspending its calendar; background expiry continues. A recurrence concurrency
revision prevents stale detached writes from undoing closure/checkpoints, including bulk restore
and goal-link updates. Failed synchronous/asynchronous saves (including cancellation) restore the
pre-save revision and modification flag; a stale detached retry cannot inherit an unsaved token.

## Consequences

### Positive

- Restart/retry/race behavior is enforced by database transactions and unique fact identity.
- Both policies share the deterministic calendar engine and preserve actual completion history.
- User isolation and read-only boundaries hold independently of reminder settings.
- Migration remains additive; ordinary task fields and external schemas stay compatible.

### Negative / Trade-offs

- Changed calendar preferences can require rescanning a long history, in bounded transactions.
- Catch-up completes over multiple transactions rather than one tenant-wide atomic snapshot.
- A tick snapshots open series IDs and active account IDs in memory; fact processing is bounded.
- Detached recurring writes can require refresh after a background checkpoint revision changes.
- Archive is a visibility choice, not a schedule pause. Trash/restore requires explicit later resume.

## Deferred

Linked replacement/resume (#91), history/unified reads (#92), reports (#93), Weekly Review (#94),
public MCP/chat (#95), UI and final integration (#96). Public recurring creation remains disabled;
this stage supplies no API, MCP schema or configuration switch that enables it.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Domain/Tasks/TaskItem.cs` | Configuration, checkpoints and administrative lifecycle invariants |
| `OwnPlanner.Application/Tasks/Recurrence/RecurringTaskLifecycle.cs` | Shared write policy and bounded catch-up |
| `OwnPlanner.Application/Tasks/Recurrence/IRecurringTaskStore.cs` | Tenant-bound transaction port |
| `OwnPlanner.Infrastructure/Tasks/RecurringTaskStore.cs` | SQLite transaction adapter |
| `OwnPlanner.Infrastructure/Persistence/AppDbContext.cs` | Mapping, constraints and concurrency token |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/RecurrenceReconciliationWorker.cs` | Trusted periodic per-account execution |
| `OwnPlanner.Mcp.StdioApp/FixedTenantRecurrenceWorker.cs` | Fixed-tenant periodic execution |
| `docs/recurring-tasks.md` | Current implementation and later-stage contracts |
