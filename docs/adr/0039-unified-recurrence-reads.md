# ADR-0039: Unified planning work and independent occurrence history

**Date:** 2026-11-13\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#92](https://github.com/am-space/own-planner/issues/92), stage 4 of
[#84](https://github.com/am-space/own-planner/issues/84), supplies shared read inputs before report,
Weekly Review and public tool integration. [ADR-0036](0036-recurrence-calendar-foundations.md),
[ADR-0037](0037-recurrence-persistence-lifecycle.md) and
[ADR-0038](0038-linked-recurrence-successors.md) establish calendar generation, read-only boundaries,
terminal facts and preservation of predecessor history. These decisions remain in effect.
The [overall plan](../recurring-tasks-plan.md) remains active through stage 8.

## Decision

Application provides `IPlanningWorkReader` and `ITaskOccurrenceHistory` implementations over a
tenant-bound `IPlanningWorkStore` snapshot port. Infrastructure loads immutable source/fact data
under one database transaction without tracking or SaveChanges. Each query resolves one shared
calendar/clock snapshot and never invokes lifecycle reconciliation, closes parents or changes
review/preferences state. Hosts register the readers internally without adding public transports.

Planning requires an explicit bounded calendar range and ordinary-task selection. Open ordinary
tasks appear once by task ID; series generate occurrences identified by their original task ID/date.
Current-plus-next periods coexist. Facts override effective expiry; unresolved expired skip-policy
dates are computed as skipped without being persisted. Closed parents, Trash and unavailable lists
are excluded from active work. Sources carry current metadata, schedule/policy and links, with manual
focus and UTC deadline once per source in the page; the adapter restores the UTC marker omitted by
SQLite's legacy DateTime mapping. Occurrences acquire no deadline or synthetic task ID.

Application merges lazy ordered source streams, then filters, exactly counts and pages the combined
work. Optional task-ID selection first narrows sources, so unrelated retained histories are neither
used to derive the fact range nor enumerated. Ordering is work date (undated last), task ID and kind.
Exact totals distinguish ordinary tasks, distinct recurring sources and occurrence/work-item counts.
Optional before-window retention scans
the complete original retain-policy prefix, subtracting every recorded fact and honoring original
generation bounds. There is no lookback cap and no pending storage. A priority queue holds the next
generated row per source rather than accumulating generated history to sort it.

History selects recorded facts independently of rule matching and generation bounds. Closed and
predecessor facts remain readable subject to the same Trash/list restrictions. Scheduled-date views
include all fact statuses in an explicit DateOnly range; activity views select completed facts by
actual UTC CompletedAt. Rich pages identify the basis and calendar, include full occurrence periods
and distinct current source metadata, and preserve exact totals. The original fact-only history
methods delegate to these queries. No parent closure is counted and no expected history is inferred.

Page limits are 1–100, offsets nonnegative, calendar selections at most 3,660 days and instant
selections at most 3,660 elapsed days. Retained prefixes are explicitly exempt from the window span
limit. Missing, mixed, backwards and non-UTC history bounds fail before data access. Every page
discloses exact totals and HasMore; no operational truncation is hidden. Complete periods must remain
representable under the existing calendar-helper limits; direct fact lookup still supports extreme
dates. A page is consistent within one call; later pages can reflect subsequent writes.

## Consequences

### Positive

- Reports and later tools can reuse one mixed projection and one independent history selection.
- Retained obligations stay reachable through ordinary pagination without persisting pending work.
- Preserved advance completions remain readable after replacement without regenerating obligations.
- Existing task-list, report, HTTP, MCP, ordinary-task and database contracts remain unchanged.
- Clock, calendar, read-only and authenticated tenant boundaries have focused test evidence.

### Negative / Trade-offs

- Exact totals scan every matching generated date, including long retained histories.
- Current sources and selected persisted facts are loaded in memory; generated work is lazy.
- The existing SQLite transaction pattern can delay concurrent writers during large reads.
- Source assignments and calendar periods reflect current metadata/preferences, not historical events.
- Deterministic offsets do not freeze a multi-request snapshot across later writes.

## Deferred

Report/preload integration (#93), Weekly Review (#94), public MCP/chat exposure (#95), browser and
final cross-layer integration (#96). Public creation remains disabled; no schema migration is needed.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Tasks/Recurrence/PlanningWork.cs` | Selection, identity, source, status, count and page contracts |
| `OwnPlanner.Application/Tasks/Recurrence/PlanningWorkReader.cs` | Shared merge/filter/count/page and effective-status projection |
| `OwnPlanner.Application/Tasks/Recurrence/TaskOccurrenceHistory.cs` | Scheduled-date and actual-completion-time fact views |
| `OwnPlanner.Application/Tasks/Recurrence/IPlanningWorkStore.cs` | Immutable tenant snapshot boundary |
| `OwnPlanner.Infrastructure/Tasks/PlanningWorkStore.cs` | Read-only transactional persistence adapter |
| `docs/recurring-tasks.md` | Current query semantics and limits |
