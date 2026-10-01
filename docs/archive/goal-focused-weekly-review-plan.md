> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0026: Goal-focused shared weekly review](../adr/0026-goal-focused-weekly-review.md).
> Details below may not reflect later refinements made during review.

# Goal-focused weekly review plan

**Status:** Implemented
**Issue:** [#68](https://github.com/am-space/own-planner/issues/68)

## Outcome

Open the shared weekly review in web or Telegram chat with active goals first, computed progress and flags, next-week commitments, then remaining task carryover and a goal-work summary.

## Acceptance mapping

| Issue criteria | Change and evidence |
| --- | --- |
| All active goals, attention first; completed/open tasks; no-step, 14-day stalled and target-period flags | Application goal review model and flag calculation from per-user SQL snapshots; Application and Infrastructure tests for ordering, thresholds, horizons, stale links and archived/trashed tasks |
| Next-week commitment for each goal; leave without a plan after one confirmation; plan an otherwise unselected task | Expose goal tasks and plan counts in `weekly_review_open`; guarded goal task focus action accepts an existing linked task; chat guidance asks once and honors a refusal; MCP and integration tests |
| Goal metric/status/target edits | Reuse goal tools in the chat workflow and refresh review; tool guidance and transport tests |
| Task review follows goals and omits already planned goal tasks; suggest links with confirmation | Filter target-week goal tasks from carryover page; expose task goal IDs; add guarded explicit link action and chat guidance; SQL and action tests |
| Completed/planned goal-work share and unplanned goals | Application-calculated counts from SQL and goal plan state; tests for active versus inactive/stale goals and week boundaries |
| No active goals invitation once; open remains nonmutating for planning data | Use first-open review state transition to expose a one-time suggestion; service tests and chat guidance |
| Counts-only invitations/reminders; active goals alone trigger a reminder | Extend counts-only notification and eligibility; service tests |
| Telegram `/review` goals with flags before tasks | Update handler and integration tests |
| Documentation | Update weekly review, Telegram and AI reference docs; record ADR and archive this plan in the completed change |

## Impact map and order

- Domain: existing `Goal` and `TaskItem` fields suffice; no schema change expected.
- Application: extend weekly review contracts, calendar calculations, service and guarded actions. Reuse `GoalService` for edits.
- Infrastructure: extend the per-user `WeeklyReviewStore` SQL read model. No AuthDbContext change or migration expected.
- Web API and frontend: additive response fields; render goal summaries before task list in the existing review settings panel. No new route or screen.
- MCP: additive fields and action options on shared `weekly_review_*` tools; both direct and stdio paths reuse them.
- Console/stdio: no separate workflow; shared tools and skill guidance apply.
- Tests: focused Application, Infrastructure, MCP and Web integration tests, followed by full `./scripts/verify.sh --all`.
- Documentation: current reference pages, ADR, archived plan.

Implementation follows the existing `WeeklyReviewSelection`/`WeeklyReviewStore` read model and `WeeklyReviewActions` guarded mutations. Keep task and goal edits explicit. Preserve per-user context resolution and existing endpoint/tool fields.

## Assumptions, exclusions and risks

- A 7-day completed window is the rolling seven days before the report instant. Stalled means no linked completion during the previous 14 days; newly created goals get a 14-day grace period.
- Target periods use strict `yyyy-MM`, `yyyy-Qn`, `yyyy` or goal target dates. “Ending soon” means an end date within seven local calendar days, inclusive; uninterpretable periods have no period flag.
- Open and completed goal-task counts exclude Trash and archived lists. Goal work means tasks linked to a currently active goal.
- No separate persistent commitment decision is required: an unplanned goal remains visible in fresh summaries. The assistant asks once before accepting a week without a plan.
- No new screen, goal hierarchy, progress history, configurable threshold or out-of-review nudges.
