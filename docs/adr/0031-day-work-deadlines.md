# ADR-0031: Daily execution with today's deadlines in Day Work

**Date:** 2026-10-02\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

---

## Context

Day Work's focus-only preload missed incomplete deadlines when a task had no today focus date.
[Issue #77](https://github.com/am-space/own-planner/issues/77) requires a compact daily view,
overdue warnings, exact counts and fresh reads without expanding mutation permissions.
[ADR-0029](0029-shared-user-calendar.md) provides local-day rules; the bounded metadata/report
pattern from [ADR-0030](0030-general-week-attention.md) provides an existing tenant-bound reader
and shared registered tool class. Neither prior contract is superseded.
The [implementation plan](../archive/day-work-deadlines-plan.md) records the acceptance mapping.

## Decision

Compose a separate daily result through Application's pure `DayReportBuilder`. Today selects
incomplete stored focus dates matching local today plus deadlines within the shared half-open UTC
bounds. Overdue selects deadlines strictly before the captured instant. Default sections deduplicate
by assigning today first, retaining all daily reasons and dates. Exact raw match and assigned counts
are distinct. Five samples per section are the default; named sections retrieve every match with
validated bounded offset/limit pages. Importance accompanies each clipped task title and date.

Add `GetDayAsync` to the existing `IGeneralReportReader` family. Infrastructure resolves fresh today
and queries eligible task metadata through the host-bound factory; composition stays inward. The
additive shared `day_report_get` tool delegates validation/read operations and accepts no period or
tenant selector. Existing web, HTTP MCP and stdio registrations expose the shared method.

Replace Day Work's internal focus-only preload with the daily report and add only this read
capability. Keep lightweight per-turn calendar context; fixed-period preloads omit calendarPeriod
arguments using `PreloadAcceptsCalendarPeriod`. Daily attention/priority requests and changes require
fresh daily reads by instruction. Focus-only questions still use the existing focus query. Explicit
instructions govern empty-day handling, bounded overdue warnings, constraints and remaining work,
with existing requested execution operations unchanged.

## Consequences

### Positive

- Due-only and differently focused tasks become visible without scheduling changes.
- Daily reasons, exact counts and bounded pages remain independent of transports and user scope.
- Importance and deadline metadata support grounded prioritization; no capacity estimates are invented.
- Existing report/tool responses and writes remain compatible; no migrations or dependencies.

### Negative / Trade-offs

- Eligible metadata is materialized to compute exact counts before sampling, matching current readers;
  larger stores may justify a dedicated database projection later.
- Offset pages are current state and can shift after changes; combined pages require ID deduplication.
- Tool selection and priority advice remain model instructions; scripted tests verify prompt delivery
  and authorized execution, not live model judgment.

## Alternatives Considered

- Extend the focus-only tool to include deadlines: changes an existing selection contract.
- Expose General's weekly attention tool in Day Work: adds irrelevant weekly and older-focus sections.
- Load individual task bodies for the initial overview: unnecessary payload; targeted detail remains available.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Reporting/DayReport.cs` | Daily contract, classification and bounds |
| `OwnPlanner.Infrastructure/Reporting/GeneralReportReader.cs` | Shared calendar and tenant-bound metadata read |
| `OwnPlanner.Mcp.Tools/GeneralReportTools.cs` | Shared additive daily tool |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Daily preload, narrow permissions and prioritization instructions |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Fixed-period preload arguments |
| `docs/day-work.md` | Living daily query and execution reference |
