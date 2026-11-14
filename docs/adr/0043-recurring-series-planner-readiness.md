# ADR-0043: Recurring series presentation and integration readiness

**Date:** 2026-11-14\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#96](https://github.com/am-space/own-planner/issues/96) completes the eight-stage implementation of
[#84](https://github.com/am-space/own-planner/issues/84). ADR-0036–0042 provide the calendar engine,
persistence/lifecycle, linked successors, projection/history, reports, review and shared MCP/chat
operations. A stored series still needs understandable Planner presentation, especially when a
successor coexists with unresolved obligations from its predecessor. The original
[implementation plan](../archive/recurring-tasks-plan.md) is preserved as history.

## Decision

Keep Planner SQL filtering, exact totals and paging at one row per persisted task/series. Extend
summary and detail DTOs additively with optional `Recurrence`: a human-readable rule summary,
DateOnly start/inclusive end, named missed-period policy, predecessor/successor IDs and outstanding
work flags. Ordinary task fields/routes/ordering remain compatible. No occurrence-expanded or
calendar UI, schedule editor or additional lifecycle button is introduced.

Infrastructure enriches only page-selected series and their immediate predecessors in the same
read transaction as the source page/detail. Related metadata omits descriptions. It loads terminal
fact dates only for available open shortened candidates. Application's
`PlannerRecurrenceProjection` reuses `IRecurrenceCalculator` and the resolved shared calendar:
any unresolved generated date in a shortened predecessor's complete bounds is outstanding under
retain policy; skip policy starts at the full current local period, so expired periods are not
pending. Closed, trashed or archived-list sources contribute no actionable obligations. Reads never
reconcile, create facts or change parent state. A flag is an existence check, not a workload count.

The Planner inspector displays rule, bounds, missed policy and explicit series/occurrence guidance.
Previous/successor buttons select the linked ID independently of current filters and persist it in
the URL; unavailable targets use existing not-found behavior. The indicator on a successor refers
to its immediate predecessor. Earlier ancestors remain reachable by following the chain. Ended
series are labelled `Series ended`. Focus/deadline metadata is explicitly separate from recurrence.

Weekly Review labels source-page units as tasks/series and independently shows exact pending
occurrence, source-series, earlier-date and target-week totals already provided by the backend.
Per-series rows and completion evidence stay distinct. Occurrence drill-down/actions remain in chat.
Trash explains that restoring a series preserves history and keeps it ended; resumption requires a
chosen-start successor. Shared task tool descriptions and the service contract document that rule.

Use existing migration/lifecycle/successor/report/transport suites alongside new store and browser
scenarios as the overall acceptance evidence. Real cookie-authenticated export snapshots preserve
links, schedules, out-of-bounds historical facts and the retry ledger, excluding the other tenant.
Deletion removes the authenticated account/database while another user's series remains accessible.
A monthly catch-up/correction/resume integration scenario checks automatic closure, independent
actual timestamps, retry stability and no fabricated break-period facts across fresh SQLite contexts.
[The evidence map](../recurring-tasks-verification.md) distinguishes deterministic routing/browser
checks from opt-in real-model evaluation.

## Consequences

- Users can distinguish linked series and previous obligations without generating persisted tasks
  or expanding lists into occurrences.
- No new migration or dependency is required. Existing recurrence migrations apply through normal
  per-user initialization; account export remains a self-contained SQLite snapshot.
- Page payloads stay bounded. Predecessor fact-date memory scales with selected candidates' history;
  retained existence checks are lazy, and skip checks seek past expired periods. This is not a
  constant-memory guarantee for arbitrarily large recorded history.
- Calendar preferences are resolved once through the existing shared service; the metadata/facts
  use the Planner read transaction. Preferences are not frozen across separate HTTP requests.
- Soft links can outlive permanent deletion; existing not-found handling applies. No related series
  is deleted or silently resumed by navigation/recovery.
- Scripted verification establishes contracts and enforcement, not a claim about a live model's
  interpretation. Publishing a release and deploying remain separate authorized actions.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Planner/PlannerReadModels.cs` | Additive series presentation contract |
| `OwnPlanner.Application/Planner/PlannerRecurrenceProjection.cs` | Rule description and outstanding-work policy |
| `OwnPlanner.Infrastructure/Planner/PlannerReadStore.cs` | Tenant-bound page enrichment and snapshot reads |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/PlannerPage.tsx` | One-row lists, details and link navigation |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/WeeklyReviewPage.tsx` | Explicit source and occurrence count units |
| `OwnPlanner.E2E.Tests/RecurringPlannerE2eTests.cs` | Desktop/mobile recovery and real account export/deletion isolation |
| `OwnPlanner.Infrastructure.Tests/Tasks/RecurringTaskSuccessorTests.Integration.cs` | Monthly downtime, correction and successor resumption |
