> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0028: Shared task goal-link suggestions](../adr/0028-task-goal-link-suggestions.md).
> Details below may not reflect later refinements made during review.

# Task goal suggestions

**Status:** Implemented

## Outcome

When chat creates tasks, offer a short, relevant link to an active goal and apply it only on the user's confirmation.

## Acceptance mapping

| Criteria from #70 | Change and evidence |
|---|---|
| One clear match; no match; at most two candidates | Shared Application instructions use semantic matching of current active goal titles/descriptions; prompt checks and scripted conversations cover single, absent and ambiguous matches. |
| Confirmation; explicit original goal | Create unlinked first; resolve explicitly named goals before creation. New focused linking service/tool changes only the goal association after confirmation; service and transport tests protect target validation and unchanged task fields. |
| Batch creation and plan decomposition | One combined sentence for successfully created tasks. Parent owns suggestions for delegated creation; specialist receives explicit goal authorization only. Scripted direct/batch/delegated tests. |
| Declines are not repeated | Conversation history records the declined task identities; shared instructions and summary instructions preserve declines and pending link choices (titles, plus IDs when available). Scripted multi-turn decline and summary checks. |
| Active goals only | goal_list includeInactive=false; linking service rechecks Active before changing a task. Cover paused, achieved, dropped and missing goals. |
| Web and Telegram, every task-creating mode | Shared mode/skill guidance for General, Week Planning, Day Work and Global Planning delegation. Day Work gains goal reads and focused linking; Global gains focused linking. Both channels already share PlanningService and user-bound tools; tenant tests cover isolated calls through web/Telegram session contexts. |
| Urgent/unrelated work, short replies | Create first; skip optional goal lookups for urgent work, omit irrelevant suggestions and append at most one short sentence. No preload/report scans. Scripted urgent request and instruction checks. |

## Impact

- Domain: reuse existing GoalStatus and TaskItem goal association; no changes.
- Application: shared instructions, focused ITaskGoalLinkService/TaskGoalLinkService, mode permissions and summary contract.
- Infrastructure: Gemini specialist and compaction instructions; no storage or migration changes.
- MCP: additive taskitem_link_goal with taskId/goalId; shared registration in web, HTTP MCP and stdio.
- Web: DI/tool registration; existing authenticated per-user context unchanged.
- Console/stdio: shared instructions and stdio registration; console calls existing MCP host.
- Frontend: no change; ordinary chat replies.
- Tests: Application service/policies, MCP schema/errors, scripted Gemini orchestration, web tenant integration.
- Docs: AI reference, ADR-0028, archive this plan and update index.

## Implementation order and patterns

1. Reuse GoalRead, request-scoped task_management and existing shared MCP handlers.
2. Add narrow Application link service, validate task/list availability and Active goal, preserve unrelated fields and existing associations.
3. Expose a thin shared tool and register in both hosts; add only required mode capabilities.
4. Add shared guidance, delegated-parent ownership, and history summarization rules.
5. Add focused tests, then full verification and completed-diff review.

## Compatibility, assumptions and exclusions

All existing routes, tools, DTOs and numeric modes remain unchanged. No schema migration or new LLM request pipeline is needed. Repeated linking to the same active goal is idempotent; conflicting existing links require fresh user direction through existing editing tools. Goal relevance and natural-language consent remain model responsibilities, consistent with existing chat skills; deterministic tests verify supplied instructions and scripted orchestration, not live model judgment. Do not use keyword scoring for semantic matches such as physiotherapy supporting a running goal. Declines belong to the current conversation, not account-wide preferences. No automatic links, bulk existing-task sweep, note linking or new goal suggestions.

## Verification

Focused Application, MCP, Infrastructure adapter and web tenant tests; build stdio and web. Final gate: ./scripts/verify.sh --all, git diff --check, and acceptance/diff review. Dependencies are already prepared and unchanged.

## Plan review

Reviewed before implementation: every criterion maps above; business validation stays in Application; authenticated repositories select the tenant; focused tool avoids giving Day Work general editing powers; all changes are additive; no migrations, personal-data logging or unrelated cleanup. Current providers require model-based matching, so scripted results must be reported with that limitation. Proceed with the reviewed plan.

## Completion review and results

Implemented the reviewed scope with additive shared guidance and focused linking. Rechecked every
criterion against the mapping above. The task-management skill supplies goal reads/linking in General
and Week Planning; Day Work and Global Planning receive only their missing focused capabilities.
Web and Telegram controller tests use the real chat factory, scripted Gemini provider, shared tool
and user-bound SQLite repositories. Current-state validation and cross-user rejection are enforced
in Application; semantic matching and natural-language consent remain model responsibilities.

`./scripts/verify.sh --all` passed: frontend lint/build, 777 backend tests and 26 browser E2E tests.
The only build warnings were existing ASP.NET test-host deprecations and the existing Vite chunk-size
notice. No live Gemini relevance evaluation was run. Completed-diff review, `git diff --check` and
local documentation target checks passed. No migrations or dependency changes were needed. ADR-0028
records the resulting design and limits; the plan is archived in this implementation PR.

## PR review follow-up plan

Two review findings require changes before merge:

1. Replace snapshot validation followed by ordinary task update with a tenant-bound repository
   operation. In one SQLite write transaction, conditionally update only GoalId/UpdatedAt while
   checking the non-trashed task, unlinked association, available list and Active goal; return a
   consistent task snapshot or an explicit failure. Cover concurrent conflicting links, stale
   eligibility and unchanged fields with actual SQLite tests.
2. Add Application-owned structured conversation state for pending goal choices and declined task
   IDs. A chat-local choice tool records offers/declines, rejects repeat offers for declined IDs,
   and the adapter injects the state into every applicable model request. Explicit reset clears it;
   rebuilding after summary, trim/failure and provider recovery preserves it. Cover real trim and
   failed-summary paths, separate sessions, repeated offers and successful/failed link cleanup.

Reviewed before follow-up implementation: these address the original criteria, keep business rules
and state in inward layers, add no tenant arguments or database schema, and preserve existing MCP
contracts. The new choice capability is local to chat, like skill_load, with mode permissions.
Natural-language choice recognition remains the model's responsibility; registered choices are
retained independently of transcript/model summaries. Re-run focused tests and full verification,
update ADR/reference/PR evidence, and resolve the findings after the fixes are pushed.

### Follow-up results

The two fixes are implemented. Atomic SQLite tests protect changed eligibility, unchanged task
fields, idempotence and concurrent conflicting links. Structured choice state is retained across
actual summary/trim/failed-summary compaction, rebuild and recovery; repeated offers for declined IDs
are rejected and successful/failed links clean up or retain pending choices appropriately.

After refreshing dependencies with `./scripts/setup.sh`, the full canonical `./scripts/verify.sh`
passed: frontend lint/build, 802 backend tests and 26 browser E2E tests, including the choice-schema
regression and web capability assertion. All modified projects compile. Documentation links and
`git diff --check` pass. No migrations were needed.
