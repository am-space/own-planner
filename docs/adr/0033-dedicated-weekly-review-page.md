# ADR-0033: Dedicated Weekly Review page

**Date:** 2026-11-11

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#85](https://github.com/am-space/own-planner/issues/85) separates the recurring planning activity
from account configuration. Previously WeeklyReviewSettings combined preferences and the review
workflow at the bottom of Settings. The shared Application workflow from
[ADR-0024](0024-shared-weekly-carryover-review.md), including subsequent goal guidance, already
provides every required API and lifecycle operation. This decision changes only its web entry
points and presentation responsibilities; it does not replace that shared workflow decision.
The [implementation plan](../archive/weekly-review-page-plan.md) records the acceptance mapping.

## Decision

Add `/planner/review` under the existing authenticated PlannerShell and add Weekly Review immediately
after Tasks in its shared desktop/mobile navigation. WeeklyReviewPage owns the existing goal/task
report, pagination and explicit finish, skip and defer controls. Its content scrolls within the
shell's bounded planner outlet and reuses the persistent assistant and mobile surface switch.

The page reads preferences before presenting a review. An explicit timezone is required; missing
timezone shows a setup link rather than guessing a timezone or opening the workflow. Disabled
reminders and missing Telegram connections do not block manual review. Loading and request errors
have explicit feedback, and an initial failure can be retried. The page uses existing apiService
methods and `/api/weekly-review` contracts, leaving calendar calculations and lifecycle rules in
Application. Opening presents the shared review and can claim existing one-time guidance; it does
not mutate tasks.

WeeklyReviewSettings now contains only preferences and a link to the review page. It is the first
Settings section, before Telegram and access tokens. The review page links back to
`/settings#weekly-review`. Existing goal evidence remains visible; this change adds no retrospective
or task-editing workflow.

## Consequences

### Positive

- A regular planning activity has a direct authenticated route and responsive navigation entry.
- Configuration and review UI have separate ownership while sharing existing web/Telegram state.
- HTTP/MCP shapes, tenant selection, persistence and reminder delivery remain compatible.

### Negative / Trade-offs

- Entering the review page presents the review immediately and consumes existing presentation
  claims when applicable, just as the former explicit Settings action did.
- Task changes still require the existing conversational workflow; no new editing controls are added.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/WeeklyReviewPage.tsx` | Dedicated workflow and scrollable report |
| `OwnPlanner.Web/ownplanner.web.client/src/components/settings/WeeklyReviewSettings.tsx` | Preference-only UI and review link |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/SettingsPage.tsx` | Preference section ordering |
| `OwnPlanner.Web/ownplanner.web.client/src/App.tsx` | Authenticated route |
| `OwnPlanner.Web/ownplanner.web.client/src/components/PlannerShell.tsx` | Shared responsive navigation |
| `OwnPlanner.E2E.Tests/WeeklyReviewE2eTests.cs` | Navigation, states and real shared API regression coverage |
| `docs/weekly-review.md` | Current user entry points and review semantics |
