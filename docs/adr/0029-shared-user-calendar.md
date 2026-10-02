# ADR-0029: Shared user-calendar periods across planning modes

**Date:** 2026-10-02\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#75](https://github.com/am-space/own-planner/issues/75) addresses mismatched UTC/rolling windows in
General, Week Planning, Day Work and Reflection. Weekly review already has explicit per-user calendar
preferences and DST handling, but its final-day targeting and frozen state are workflow-specific.
The [implementation plan](../archive/shared-user-calendar-plan.md) records the acceptance mapping.
This decision supersedes the UTC-only mode period decisions in [ADR-0013](0013-deterministic-weekly-workload-reporting.md)
and [ADR-0014](0014-deterministic-current-state-reflection-reporting.md), retaining their legacy report
contracts, evidence selection and historical limitations.

## Decision

Application's `PlanningCalendarResolver` resolves named periods into separate half-open focus-date
and UTC-instant ranges. `PlanningCalendar` reads `IWeeklyReviewStore.GetPreferencesAsync` and
`TimeProvider` on every call. Explicit timezone/week start applies independently of reminder opt-in.
A missing timezone uses a disclosed UTC fallback, retaining week start (default Monday); no settings
or review state is written. Shared `CalendarRules` implements week starts and local boundary conversion.
`WeeklyReviewCalendar` delegates its pure helpers without changing review creation/targeting or state.

Supported names are today, thisWeek, remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays and
lastSevenDays. Calendar weeks use the configured week start and never advance to next week on the
last day. Next seven days begins today. Last seven days is the rolling 168 hours ending now; its
focus-date range covers the local dates touched by that interval. A local midnight exclusive end
excludes its date. Midnight gaps advance to the first valid minute, and overlaps select the earlier
instant, matching weekly review. Focus values are not shifted as instants; deadline/completion/note
timestamps use the local UTC boundaries. Calendar weeks need not be 168 hours.

Shared MCP report tools add optional `calendarPeriod`. Omission preserves existing UTC defaults.
Conflicting explicit legacy date/range overrides fail instead of silently changing interpretation.
Weekly/reflection results include `calendar` metadata, actual timezone and period semantics. Their
existing bounded samples, exclusions, current-state signals and historical limitations remain.
The focus-date tool accepts the additive today selection and returns local calendar metadata alongside
unchanged page fields; its legacy path retains its old shape. General keeps every original UTC field
and adds a separate bounded calendar today/requested-period summary. #74's full attention overview
remains separate. `calendar_period_get` is an additive read-only metadata tool with no tenant selector.

Mode preloads explicitly select thisWeek for General/Week Planning, today for Day Work and lastWeek
for Reflection. Application refreshes small calendar metadata before every affected turn, after any
compaction, and replaces the adapter's per-request context. `ChatServiceAdapter` places this metadata
in every model request/tool round, outside replayed history; explicit reset clears it. Shared calendar
instructions require fresh named-period data on new date-scoped questions, explain fallback and
actual dates/timezone, and distinguish calendar weeks from rolling ranges. Request context is clock
metadata, not a new entity preload. Interpretation and tool selection remain model behavior; tests
exercise actual serialized requests with scripted provider responses.

Web direct execution, HTTP MCP and stdio register the same handlers and calendar service through
existing tenant-bound factories. No new HTTP route, storage schema, dependency, UI, reminder or
weekly-review transition is introduced. Fixed-clock tests cover offsets, week/year boundaries, DST
gaps/overlaps, half-open task membership, fresh calls across midnight, disabled/unconfigured
preferences, old UTC contracts, bounded samples and two users with different calendars.

## Consequences

- Calendar language and the initial report agree across the four planning modes.
- Existing external defaults stay compatible while the chat workflow explicitly selects local periods.
- One read-only preferences lookup per turn supplies fresh clock context, including after midnight.
- Model selection of a fresh report and natural-language ambiguity handling still require instruction
  compliance; scripted tests do not establish live Gemini quality.
- General temporarily carries both legacy UTC data and separate local data, increasing its bounded
  initial context. Consumers must use the labelled calendar section for local questions.

## Deferred

The #74 attention overview, deadline expansion in Day Work, new Reflection classifications or write
permissions, capacity modelling, scheduling and reminders remain excluded.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Calendar/PlanningCalendar.cs` | Pure resolution, shared boundaries and read-only preferences/clock service |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Explicit preload arguments and fresh request context |
| `OwnPlanner.Application/Chat/CalendarGuidance.cs` | Named-window selection, fallback and refresh instructions |
| `OwnPlanner.Infrastructure/Reporting/*ReportReader.cs` | Tenant-bound date and instant membership |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Per-request metadata injection |
| `OwnPlanner.Mcp.Tools/PlanningCalendarTools.cs` | Shared read-only period getter |
| `docs/user-calendar.md` | Calendar meanings and compatible tool contracts |
