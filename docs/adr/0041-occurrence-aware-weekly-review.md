# ADR-0041: Occurrence-aware Weekly Review with atomic guarded actions

**Date:** 2026-11-14\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

## Context

[#94](https://github.com/am-space/own-planner/issues/94) integrates the shared recurrence projection
and actual completion evidence into goal-first Weekly Review. Task-only focus/completion queries
cannot represent several occurrences of one series, and ordinary review completion would end the
parent. The [overall recurrence plan](../archive/recurring-tasks-plan.md) remains active through #96.
This extends [ADR-0026](0026-goal-focused-weekly-review.md) and reuses
[ADR-0039](0039-unified-recurrence-reads.md) and [ADR-0040](0040-occurrence-aware-reports.md).

## Decision

Weekly Review keeps its frozen target date, timezone and UTC boundaries. The target's weekday
recovers the frozen week start; preference edits do not retarget an existing review. The shared
projection enumerates pending current-week/month occurrences, target-week occurrences and the
complete retained prefix. Sunday next-week review never prematurely expires current-period work.

`Report.Tasks` remains a unique stored-source page, with ordinary and recurring records counted
once. Series rows add recurrence and pending/planned occurrence counts. A series already planned
for an active goal is omitted only when no earlier actionable occurrence needs review. Its real
deadline remains source metadata, never a deadline copied to every occurrence.

`Report.Occurrences` is a separate first page of pending original identities, with exact totals,
distinct-series/carryover/planned counts, independent offset/limit, full periods and bounded source
metadata. `weekly_review_occurrences` and authenticated `GET /api/weekly-review/{id}/occurrences`
provide independent paging and optional task filtering without opening a review or claiming guidance.
A targeted `(taskId, occurrenceDate)` lookup also reaches recorded facts on closed predecessors,
including facts outside shortened generation bounds. A targeted pending lookup projects one date,
without scanning its entire retained prefix while holding the writer lock.

Goals add shared `Work` evidence, per-goal `PlannedWork`, and a `WorkShare` split between currently
active goals and other work. Counts distinguish ordinary tasks, distinct series and occurrences.
Target-week plans count pending original dates; ordinary commitments retain focus-date selection.
Legacy completed-task counts/samples exclude series closure and remain ordinary-task evidence;
legacy open/planned counts describe stored sources. Stalled flags include actual occurrence
completion in the existing rolling fourteen-day window, preserving creation/resume grace. No-plan
flags use exact pending target-week occurrences independently of samples. Completion activity uses
actual UTC `CompletedAt` in the existing inclusive rolling seven-day window, represented as
`[now - 7 days, now + 1 tick)`; scheduled outcomes use target-week calendar dates in the frozen zone.
These are distinct selections, not two quantities to sum or estimates of effort/capacity.

`weekly_review_apply` adds explicit `completeOccurrence` / `cancelOccurrence` actions, original
`occurrenceDate` and a fresh `guardToken`, retaining the existing required arguments. The token
hashes the review calendar, source timestamp/recurrence revision, schedule and addressed occurrence
state/period. UTC instants used from persisted review boundaries are normalized by ticks so SQLite
DateTime.Kind changes cannot invalidate unchanged snapshots. This is a stale-state guard, not an
authorization credential or approval of successor conflict choices.

One non-deferred tenant SQLite transaction re-reads review state, current parent/list, revision,
schedule and fact, checks the guard, then applies the shared `RecurringTaskLifecycle` terminal-fact
transition. A stale/finished/unavailable target returns `Applied=false` and requires refresh.
Ordinary completion/focus/Trash/deadline review actions reject recurring parents. A reviewed
occurrence never calls `TaskItemService.CompleteAsync(parentId)`, reschedules a date, changes a
rule, closes the series or transfers successor work. Explicit completion of a recorded skipped fact
remains possible on a closed predecessor without reopening it. Completion defaults to the injected clock; optional explicitly reported `completedAt` uses the
shared UTC/not-in-future validation and is rejected for other actions. Concurrent attempts preserve
the first successful completion timestamp. The ordinary action transaction boundaries remain unchanged.

Browser and Telegram summaries consume mixed completion/planning counts. The browser retains one
row per series and deduplicates completed source-title samples; occurrence details/actions remain
in chat. Shared weekly-planning guidance grants the new read only where review tools already exist.
Reminder preferences, delivery claims, paused-goal mentions, one-time offers and review transitions
retain their existing behavior. No migration or new dependency is required.

## Consequences

- Current obligations, future plans and actual completion activity keep their original identities.
- Atomic review guards cover facts and schedules that an ordinary parent timestamp alone misses.
- Source metadata and selected facts are loaded in memory; generated obligations are streamed and
  page/sample payloads are bounded. Exact retained-prefix counts still cost CPU and hold a database
  snapshot, so large histories can delay writers.
- Source paging is fresh current-state paging. Ordinary and occurrence pages have separate offsets;
  no cross-request snapshot, batch rollback or historical assignment reconstruction is promised.
- Public recurring-series creation/general recurrence tools remain #95; broader series UI and final
  integration/overall plan archival remain #96. No release or deployment is part of this decision.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewOccurrences.cs` | Frozen projection selection, exact details and stale-state guards |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewActions.cs` | Explicit guarded occurrence actions and ordinary-series rejection |
| `OwnPlanner.Application/Tasks/Recurrence/RecurringTaskLifecycle.cs` | Shared transactional fact policy |
| `OwnPlanner.Infrastructure/WeeklyReviews/WeeklyReviewStore.cs` | Source rows and goal/work evidence |
| `OwnPlanner.Infrastructure/WeeklyReviews/WeeklyReviewStore.Occurrences.cs` | Tenant snapshot and atomic write callback |
| `OwnPlanner.Mcp.Tools/WeeklyReviewTools.cs` | Shared additive read/action contracts |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/WeeklyReviewController.cs` | Authenticated occurrence drill-down |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/WeeklyReviewPage.tsx` | Mixed counters and one source row per series |
