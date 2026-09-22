> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0024: Shared local-calendar weekly carryover review](../adr/0024-shared-weekly-carryover-review.md).
> Details below may not reflect later refinements made during review.

# Weekly reminders and carryover review

Status: Implemented. Issue: [#62](https://github.com/am-space/own-planner/issues/62).

## Outcome
Offer an opt-in, counts-only Telegram reminder and the same persisted local-calendar weekly review in web and Telegram chat.

## Reviewed design
- Store preferences, review lifecycle, and delivery claims in the per-user AppDbContext. Central account/link records are used only by trusted host wiring to enumerate active users and resolve current destinations. No model-supplied tenant identity.
- Application owns calendar boundaries, validation, selection reasons, transitions, and eligibility. Infrastructure provides bounded SQL queries, transactional state persistence and delivery. The web host supplies user scopes and a periodic worker.
- FocusAt is a calendar date stored as a UTC-labelled DateTime: compare its date fields without timezone conversion. Deadline instants use half-open local weeks converted to UTC.
- Default Monday week start and 18:00 on the last day. Timezone must be explicitly selected. Invalid DST times advance to the first valid minute; repeated times use the earlier instant.
- A review freezes its calendar boundaries. Calendar preference changes reuse an overlapping target calendar week to suppress duplicate prompts. Subsequent non-overlapping weeks use the new preferences.
- Delivery is claimed transactionally before network I/O. Successful delivery, chat offers, and review completion are separate. Deferral starts a new occurrence attached to the original review. Expired weeks are never replayed. Ambiguous sends are not automatically retried; known rejection may retry within a bounded policy. Missing destinations retain chat fallback.
- Add shared MCP preference/review tools and weekly-planning guidance; retain existing task tools and #61 deadline clearing. Refresh before applying user-authorized changes and report partial failures. No new mode or agent.
- Add Settings controls and review actions; Telegram chat exposes the same tools. Contextual offers are restricted to suitable planning conversations, after answering the immediate request, and claimed once across channels.

## Acceptance mapping and verification
| Criteria | Change / evidence |
| --- | --- |
| Opt-in, timezone/week/time/channel settings | Validated Application preferences, HTTP/MCP, Settings UI; default/invalid/roundtrip tests |
| End-of-week time, calendar and DST | Pure calendar functions and controllable clock tests, year/week-start/UTC edge cases |
| Carryover/deadline/target-week selection | SQL counts, deduplicated bounded samples with reasons, paging; excluded/empty/boundary tests |
| No implicit task mutations, focus/deadline separation | Read/state tools separate from existing authorized task tools; scripted chat and #61 regression tests |
| Fresh data and partial failures | Fresh review reads, targeted pre-mutation reads and confirmed results; integration coverage |
| Shared lifecycle and no repeated prompts | Per-user transactional review/occurrence state; complete/skip/defer/disable/restart/concurrency tests |
| Safe delivery and missed review fallback | Current linked destination, claimed occurrence, bounded retry, expiration; fake sender tests |
| Tenant isolation | Two-user scoped reads, settings/state, scheduled destinations and task mutation tests |
| Deterministic CI | Fake TimeProvider, scripted model and delivery; no live external services |
| Migration and docs | CLI-generated AppDbContext migration, reference docs, ADR, archived plan |

## Impact and order
Domain: preferences and review state. Application: calendar, contracts, service and policies. Infrastructure: transactional store, query and dispatch. Web: trusted scheduled execution, endpoints, registrations. MCP/stdio: additive shared tools. Frontend: Settings controls. Tests: affected layers and browser flow. Docs: references and ADR.

Implement inward layers, then persistence/migration, shared tools/host delivery, UI/chat, tests and docs. Closest patterns: GeneralReportReader, Telegram integration, DirectToolMcpAdapter and user initialization.

## Validation and exclusions
Run setup, focused suites, full `./scripts/verify.sh --all`, migration review and diff review. No deployment/release, autonomous task changes, permanent deletion, new delivery channels, or changes to existing UTC report contracts.

Plan review: acceptance criteria mapped; inward dependencies and tenant scopes preserved; additive contracts; generated migration required; no notification task titles or secrets. Continue within the authorized issue scope.

## Implementation record

All slices are implemented. Review actions use `weekly_review_apply` with a fresh revision and the
existing task services; this extends the initial plan's targeted-read approach with an Application
precheck and per-task results. Report selection expressions and composition live in Application,
with Infrastructure evaluating counts and pages in SQL. `/review` commands expose lifecycle in any
Telegram mode. Existing scheduled times remain frozen across edits; a delivered deferral cannot
block the following week's reminder. Reminder text includes the target date, which can select an
exact persisted review when several periods are available.

Focused evidence: `WeeklyReviewCalendarTests` (local weeks and DST), `WeeklyReviewTests` (SQL selection,
claims, restarts, deferrals, preference edits and action guards), `WeeklyReviewToolsTests` (schemas and
validation), `WeeklyReviewIntegrationTests` (trusted tenant scopes, destinations, shared commands,
scripted offers and partial failures), and `WeeklyReviewE2eTests` (settings, lifecycle, isolation and
unauthenticated denial). Actual HTTP MCP and stdio smoke checks exercise opt-in, report retrieval,
guarded deadline clearing with explicit null serialization and persisted lifecycle.

Final verification: `./scripts/setup.sh` and `./scripts/verify.sh --all` passed (18 browser E2E
tests, frontend lint/build and backend suites). After the final preference-preservation change,
`./scripts/verify.sh --backend` passed all 695 backend tests, and actual authenticated HTTP MCP
and stdio smoke checks passed again. Changed documentation links and the complete diff were checked. No live Telegram/Gemini calls are required. Existing npm audit, Vite bundle-size,
and ASP.NET obsolete-test-host warnings remain unrelated to this change. See ADR-0024 for the
best-effort delivery/offer policy and concurrent-edit limits.

PR review follow-up (2026-09-22): bounded dispatcher concurrency to four isolated user scopes; made
settings reads non-mutating and annotated them for MCP; corrected parameter-specific ID errors;
separated Infrastructure storage rows from Application workflow snapshots. A CLI-generated empty
migration preserves existing data and updates EF's model snapshot. Regression coverage includes
slow sends, the concurrency bound, cancellation/disposal, interleaved tenant delivery, upgrade data
preservation, read-only defaults and MCP annotations/errors.

Review-fix validation: setup and full verification passed (705 backend tests, 18 browser E2E tests,
frontend lint/build). Real HTTP MCP and stdio checks confirmed the getter's safety annotations and
unchanged review/deadline result shapes. EF reports no pending model changes; upgrade coverage
preserves the previous migration's stored preferences and review fields.
