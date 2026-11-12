# ADR-0036: Internal recurrence contracts and bounded calendar calculation

**Date:** 2026-11-12\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#89](https://github.com/am-space/own-planner/issues/89), stage 1 of
[#84](https://github.com/am-space/own-planner/issues/84), needs deterministic recurrence foundations
before persistence and public creation. The [overall plan](../recurring-tasks-plan.md) remains active
because stages 2–8 are not shipped. [ADR-0029](0029-shared-user-calendar.md) already defines local
calendar preferences, disclosed UTC fallback, half-open periods and DST boundary handling.

## Decision

Domain adds immutable validated `RecurrenceRule`, `RecurrenceSchedule`, `RecurringSeries`,
`TaskOccurrenceId` and `TaskOccurrenceFact` contracts without modifying `TaskItem` or adding EF
dependencies. Rule intervals are positive, day selections are copied/normalized, and missed-period
policy is explicit. A completed fact requires an actual UTC completion instant; skipped/cancelled
facts cannot carry one. Pending occurrences have no fact.

Application's `IRecurrenceCalculator` implementation lazily enumerates distinct ordered DateOnly
dates in an explicit half-open range intersected with inclusive series bounds. It seeks directly
to the relevant active weeks/months. The start week/month anchors intervals; monthly selected
dates clamp independently and deduplicate without changing the original rule.

Weekly alignment captures week start on the rule, keeping generated dates stable under subsequent
calendar preference changes. Reporting periods, today and unresolved-period expiry use the current
shared calendar. Preference changes do not rewrite stored facts. `RecurrenceCalendar` uses a
resolved `PlanningPeriod` and existing `CalendarRules` helpers for current-plus-next windows and
full period boundaries/expiry; it never converts an occurrence date into a timestamp or shortens
its expiry to a series cutoff.

`IRecurringTaskLifecycle` and `ITaskOccurrenceHistory` define future tenant-bound write/read ports,
including retry-safe completion/correction and separate scheduled-date/completion-time history.
There are no registered implementations. [The recurrence reference](../recurring-tasks.md) specifies
later checkpoint, reconciliation, pagination, successor conflict and activation boundaries, with
examples distinguishing required future behavior from implemented foundations.

No persistence mapping/migration, external API/MCP contract, host wiring, report, ordinary task
behavior or browser UI changes. Public recurring-task creation stays disabled until later lifecycle,
report and Weekly Review safeguards are complete.

## Consequences

### Positive

- One calendar engine is available for later lifecycle, tools and reports without duplicate arithmetic.
- DateOnly schedules and UTC completion instants remain separate and enforceable.
- Weekly/monthly interval anchoring, clamping, boundaries and DST expiry have focused test evidence.
- Internal foundations can merge independently while ordinary task contracts remain compatible.

### Negative / Trade-offs

- The snapshot/ports do not yet persist or execute lifecycle behavior; later stages must deliver
  atomicity, isolation, downtime catch-up and activation safeguards.
- Rule alignment and the current reporting week start can differ after preference changes; this
  preserves scheduled dates but can change unresolved-period grouping/expiry. Future reconciliation
  checkpoints must account for calendar configuration changes without erasing resolved history.
- Half-open DateOnly ranges and complete period helpers cannot represent a boundary beyond year 9999.

## Deferred

Persistence and lifecycle (#90), successor replacement/resumption (#91), unified queries (#92),
reports (#93), Weekly Review (#94), public MCP/chat exposure (#95) and UI/final verification (#96).
The full feature plan is archived only when those stages complete.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Domain/Tasks/RecurrenceRule.cs` | Rule and missed-policy invariants |
| `OwnPlanner.Domain/Tasks/RecurrenceSchedule.cs` | Bounds and linked series snapshot |
| `OwnPlanner.Domain/Tasks/TaskOccurrenceFact.cs` | Composite identity and completion instant contract |
| `OwnPlanner.Application/Tasks/Recurrence/RecurrenceCalculator.cs` | Pure bounded generation port and implementation |
| `OwnPlanner.Application/Tasks/Recurrence/RecurrenceCalendar.cs` | Shared-calendar windows and full-period expiry |
| `OwnPlanner.Application/Tasks/Recurrence/IRecurringTaskLifecycle.cs` | Future atomic write port |
| `OwnPlanner.Application/Tasks/Recurrence/ITaskOccurrenceHistory.cs` | Future read-only history port |
| `docs/recurring-tasks.md` | Foundation reference and later-stage engineering contracts |
