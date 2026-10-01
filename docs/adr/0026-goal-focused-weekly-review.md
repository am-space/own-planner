# ADR-0026: Goal-focused shared weekly review

**Date:** 2026-10-01

**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

[#68](https://github.com/am-space/own-planner/issues/68) extends the shared review from
[ADR-0024](0024-shared-weekly-carryover-review.md). Goals and task-to-goal soft links already live
in each user's planner database. The existing review has frozen local-week boundaries, bounded task
pages, explicit lifecycle controls and guarded task actions. The
[implementation plan](../archive/goal-focused-weekly-review-plan.md) maps the issue criteria.

## Decision

The weekly review opens with all active goals, ordered by calculated attention flags, then presents
remaining carryover and deadline tasks. `WeeklyReviewStore` reads exact counts and bounded title
samples from the user-bound `AppDbContext`. `WeeklyReviewGoalCalculator` owns the 7-day completion,
14-day stall, no-next-step and target-period rules. It also calculates the split of recent completed
and target-week planned tasks between active goal work and other work. Trash, archived lists and
inactive or missing goal references never count as active goal work. The target-period warning uses
a seven-local-day window and strict month, quarter, year or date parsing.

The existing `weekly_review_open` and authenticated HTTP response gain additive goal data and a
first-open `suggestCreatingGoals` hint. No persistent state or migration is added: the established
`notStarted` to `inProgress` transition supplies the once-per-review hint. The existing task page
omits tasks already linked to an active goal and focused in the target week, avoiding a second pass.

The shared `weekly_review_apply` tool gains `goalFocus` and `linkGoal` actions. Both reuse review,
task-list and task-revision guards; `goalFocus` additionally requires that the task is linked to
the specified active goal, while `linkGoal` requires an unlinked task and an active goal. Existing
goal tools handle metric, status and target-period changes. Week Planning gains goal create/update
capability for this conversation flow. Chat instructions require explicit user choices and one
confirmation before leaving a goal without a plan.

Counts-only reminder and chat invitation text includes active goal and flag counts when goals
exist, without titles. An active goal makes an otherwise empty task review eligible for a reminder.
Telegram `/review` lists goal summaries and flags before remaining tasks. The existing settings
panel displays goal summaries before its task page; no new screen or route is introduced.

## Consequences

### Positive

- Web, Telegram and stdio use the same Application rules and shared MCP action contract.
- Goal flags and work shares come from current planner data rather than model guesses.
- Tenant isolation continues through the existing user-bound context factory.
- All changes to planning data still require explicit user instructions.

### Negative / Trade-offs

- The goal read model uses several local SQLite queries per active goal to keep counts exact and
  title samples bounded. A very large active-goal set makes opening slower; revisit with grouped
  SQL if that becomes common.
- A review consists of fresh sequential task and goal reads, so concurrent edits between them can
  briefly make the two sections differ. Refreshing obtains current evidence.
- Matching an unlinked task to a goal and conversational confirmation remain model-guided; the
  guarded write action enforces active-goal, task and revision conditions.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewGoalCalculator.cs` | Goal flags and summary rules |
| `OwnPlanner.Infrastructure/WeeklyReviews/WeeklyReviewStore.cs` | Per-user evidence and task selection |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewActions.cs` | Guarded goal planning and linking |
| `OwnPlanner.Mcp.Tools/WeeklyReviewTools.cs` | Shared additive tool options |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | Goals-first conversation flow |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/WeeklyReviewTelegramHandler.cs` | Telegram summary ordering |
| `docs/weekly-review.md` | Current user and transport behavior |
