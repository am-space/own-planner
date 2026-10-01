# ADR-0027: Paused goals and shared focus guidance

**Date:** 2026-10-01\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

---

## Context

[#69](https://github.com/am-space/own-planner/issues/69) follows the goal-focused review in
[ADR-0026](0026-goal-focused-weekly-review.md). Temporarily inactive goals should remain available
without generating active-goal flags. Users also need gentle guidance when taking on too many goals.
The implementation plan is preserved in [the archive](../archive/paused-goals-plan.md).

## Decision

Append Paused=3 without changing existing numeric statuses. Domain records PausedAt when an active
goal is paused, clears it when leaving Paused, and records LastResumedAt when resuming into Active.
Repeated statuses preserve timestamps; achieved/dropped goals cannot be paused. Existing terminal
status operations remain supported. Application's stalled calculation uses the latest resume or
creation instant and requires at least 14 elapsed days without recent completed task evidence.

Reuse shared goal_update/goal_list across transports. Task links remain intact. Existing active-only
queries exclude paused goals from review, flags, reminders and strategic reports. Add a Paused filter
to the existing read-only planner surface. Successful creation/activation above five active goals
returns advisory activeGoalWarning. No count rejects an action; unrelated edits do not warn again.

Application claims a once-per-review warning and a once-per-local-month paused-goal mention during
live first-page presentation. Store the warning flag on the review and the latest claimed calendar
month in per-user preferences. The opening instant and review's frozen timezone determine the month.
No paused goals means no claim. Internal skip/defer/finish lookups, pagination, expired and terminal
reviews do not claim guidance. Settings, chat and Telegram /review use the same atomic SQLite
transitions. Shared weekly_review_open adds optional present=false for internal transition lookups.

Application formats one sentence with pause ages and a resume invitation. Transport views render the
returned fields; chat instructions place the warning at the beginning and mention paused goals once.
Reminders and fallback invitations never query or present paused-goal titles. AddPausedGoals is a
CLI-generated additive AppDbContext migration; central auth and tenant resolution are unchanged.
Existing SQLite exports include pause status, timestamps and claims.

## Consequences

### Positive

- Goals can remain relevant while temporarily on hold, with deliberate resume and preserved links.
- Reviews stay focused, and guidance is shared and does not repeat across channels or restarts.
- Existing stored status values, required tool arguments and route contracts remain compatible.

### Negative / Trade-offs

- A failed presentation after a persisted claim can lose that prompt, as with existing chat offers.
- Advisory active counts and goal samples can change under concurrent goal edits after retrieval.
- Pause ages use elapsed days and approximate weeks/30-day months rather than exact calendar months.

## Deferred

Automatic resume, timed pauses, a configurable goal limit and new editing screens remain excluded.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Domain/Goals/Goal.cs` | Status transitions and timestamps |
| `OwnPlanner.Application/Goals/GoalService.cs` | Successful-action warnings |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewService.cs` | Shared claims and presentation |
| `OwnPlanner.Infrastructure/WeeklyReviews/WeeklyReviewStore.cs` | Per-user serialized persistence and goal evidence |
| `OwnPlanner.Mcp.Tools/GoalTools.cs` | Shared pause/resume contract |
| `OwnPlanner.Mcp.Tools/WeeklyReviewTools.cs` | Presented versus internal review lookups |
