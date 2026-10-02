> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0030: Today-first current-week attention in General](../adr/0030-general-week-attention.md).
> Details below may not reflect later refinements made during review.

# General current-week attention

**Status:** Implemented
**Issue:** https://github.com/am-space/own-planner/issues/74

## Outcome

Broad General attention requests give a fresh, bounded today-first local-calendar week overview,
including unfinished focus plans and deadlines, with exact counts and explicit drill-down.

## Impact

Application: attention composition, DTOs, reader contract and General instructions/policy.
Infrastructure: reuse General's eligible metadata projection and bound calendar service.
MCP: additive read-only query in the existing shared General tool class.
Web and stdio: existing class registration exposes the shared handler; validate both schemas and
web tenant execution. Console uses the same mode/tools. Domain, HTTP routes, frontend, database
schema, authentication and weekly-review lifecycle: unchanged.

## Acceptance mapping and implementation order

1. Application builder classifies incomplete eligible tasks into today, earlier this week,
   remaining week, overdue deadlines and older focus. All reasons and focus/deadline dates survive;
   default display uses this precedence to deduplicate. Exact reason counts are separate from
   assigned-section counts. Remaining days expose exact per-day counts. Tests cover focus-only,
   overdue age, overlaps, exclusions by completion, empty sections and deterministic sampling.
2. Add nullable attention to General's calendar summary only for thisWeek, retaining all existing
   fields. A dedicated general_attention_get query supports fresh reads and bounded section pages;
   a specific-section query includes all matches, even those assigned elsewhere in the default
   overview. Default olderFocus sample is empty but its count is exact. Tests cover limits, offsets,
   explicit truncation, title bounds and complete retrieval of overlapping reason sets.
3. Reader reuses the current eligible projection (Trash/archive excluded), resolves thisWeek and
   today with the same as-of clock, and never writes settings/reviews. Fixtures cover midweek,
   final day, non-Monday starts, Tokyo/negative offsets, half-open local deadline boundaries,
   DST, refresh after completion and midnight, compatibility and no review/settings writes.
4. Shared MCP handler validates Application options and propagates cancellation. Verify read-only,
   optional schema arguments, no tenant selectors, error shape, stdio SDK schema and actual
   authenticated two-user DirectTool execution. Existing General tool arguments stay unchanged.
5. General declares the query immediately. Broad attention requests fetch it freshly and present
   today first, omit empty secondary sections, group future work by local day, state limitations
   and offer section pages. Explicit day requests use today only; exploratory chat does not fetch
   attention automatically. No rescheduling/mode switching/review opening. Test policy and scripted
   provider requests for broad/day/exploratory/refresh routing, distinguishing scripts from live AI.
6. Update reference docs and accepted ADR-0030; archive this plan via git mv and update docs index.

## Reviewed decisions

Dedicated query permits section pagination without extending or overloading existing report
arguments. Its default overview is also included in the existing thisWeek preload. Calendar logic
comes exclusively from ADR-0029; no new calendar preference or review targeting rules. All business
classification is Application code, not EF queries or tool handlers. Metadata excludes bodies and
descriptions and clips titles. Specific-section pages intentionally allow repeats across different
queries; the default overview deduplicates globally. Pagination is current-state, not a frozen cursor.

Plan review completed before implementation: every criterion mapped above; inward dependencies,
shared transport, tenant binding and additive compatibility checked. No material product ambiguity,
migration or expanded scope requires approval.

## Verification and exclusions

Run focused Application, Infrastructure, MCP and Web tests while iterating, then
./scripts/verify.sh --all and full diff/acceptance review before commit/push/PR to master.
Dependencies are prepared and unchanged. Exclude automatic writes, new reminders, review lifecycle,
new modes, Day Work deadline expansion (#77), Reflection actions and unrelated diagnosis changes.

## Completed acceptance review and verification

- Application fixtures establish section precedence, all reason/date annotations, exact overlap
  counts, bounded samples, future-day counts, empty/older-only output and complete section paging.
- Infrastructure fixtures establish eligibility, local/DST half-open boundaries, midweek/final-day
  behavior, disabled preferences, no review writes, unchanged legacy fields and fresh completion/
  midnight reads. UTC deadline kind is normalized in new samples after SQLite materialization.
- MCP tests establish optional read-only/idempotent schema, validation before reads, paging options,
  cancellation and errors. Existing General tool arguments remain unchanged. Existing shared class
  registration exposes the operation in stdio and HTTP MCP; web execution/schema is exercised with
  two separate user databases and different preferences, then completion and refresh.
- Scripted conversations establish immediate declarations, broad/all versus day-only routing,
  repeated reads, page calls and specialist denial. Existing exploratory-conversation tests verify
  no automatic attention query or write. Live Gemini behavior was not evaluated.
- Full ./scripts/verify.sh --all passed: frontend lint/build; 887 backend tests (Application 376,
  Domain 39, Infrastructure 258, MCP 83, Web 131); 26 browser E2E tests, no skips.
- Existing Vite chunk-size and ASP.NET test-host deprecation warnings remain. No new dependencies,
  routes, migrations, permissions for mutation or review lifecycle changes.
- Full diff and acceptance review complete; documentation describes the implemented contract.
