> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0031: Daily execution with today's deadlines in Day Work](../adr/0031-day-work-deadlines.md).
> Details below may not reflect later refinements made during review.

# Day Work deadlines plan

Status: Implemented. Issue: [#77](https://github.com/am-space/own-planner/issues/77).

Provide a compact fresh daily execution overview of incomplete planned/due-today tasks and overdue warnings using the shared user calendar.

## Acceptance mapping

| Outcome | Change and evidence |
| --- | --- |
| Focus-only, due-only, differing focus dates and overlapping reasons | Pure Application daily builder; task date/reason annotations and importance; classification tests |
| Exact bounded counts, deduplication, empty results and more tasks | Today-first overview with today/overdue precedence; raw matching and assigned counts; named-section paging; bounds/ordering tests |
| Completed, Trash and archived exclusions | Existing eligible metadata query through tenant-bound factory; SQLite reader tests |
| Local dates, midnight and DST | Resolve today once through IPlanningCalendar; stored focus dates stay dates; half-open instant deadlines; boundary/refresh tests |
| Fresh repeated requests and completion | Day Work daily preload and directly available daily read; instructions and scripted provider read/mutation/read sequences |
| Useful prioritization, empty-day behavior and narrow scope | Importance plus deadline metadata; explicit instructions consider constraints and remaining work, clarify only necessary gaps; permission snapshot and provider prompt assertions |
| Compatible shared transports and isolation | Add daily method to registered shared report tool class; MCP schema/error/cancellation tests and two-user in-process integration |

## Impact and implementation order

1. Application: daily DTO/options/builder and reader contract, reusing established bounded title and section semantics. Add importance to eligible internal metadata without changing existing report responses.
2. Infrastructure: add a daily read to the existing IGeneralReportReader/GeneralReportReader report family using its eligible task metadata query and shared calendar. No new DI registration, schema changes or migrations.
3. MCP: additive day_report_get method in the existing shared report tool class, thin validation/delegation; existing web/HTTP MCP/stdio registration exposes it.
4. Chat: replace Day Work's focus-only preload with daily report, add only the read permission, preserve execution tools. Focus-only requests retain the existing tool. Fresh daily query per relevant request, not every exploratory turn.
5. Tests in Application, Infrastructure, MCP and Web Server. Documentation reference, ADR-0031 and archived plan.

Domain, HTTP routes, console/stdio flow and frontend require no feature edits. Existing external report and task shapes remain unchanged. No dependencies, mutation tools, duration estimates, automatic scheduling, weekly backlog review or calendar preference work.

## Verification and risks

Run focused builder, reader, schema, conversation and tenant tests, then full ./scripts/verify.sh --all before push. Dependencies are prepared and unchanged. Inspect full diff and docs links. Fresh offset pages can shift after concurrent changes; disclose this and deduplicate combined pages. Samples are deterministic metadata, not a capacity ranking; prioritization remains model-guided and scripted tests do not establish live Gemini quality.

## Plan review

Reviewed before feature code: every criterion has behavior/test evidence; daily composition belongs in Application, persistence stays tenant-bound and transport handlers delegate. Reuse the existing report reader and registered tool class to avoid redundant wiring. The daily shape exposes only today/overdue reasons and importance; no weekly sections or new writes. All changes are additive except the internal Day Work preload selection. Scope, cancellation, compatibility and sensitive-data checks pass. No material product ambiguity remains.

## Implementation and verification evidence

Implemented the reviewed slice with day_report_get and the existing reader/registered shared tool
class. Full ./scripts/verify.sh --all passed: frontend lint/build, 920 backend tests (Application 389,
Domain 39, Infrastructure 269, MCP 91, Web Server 132), and 26 browser E2E tests, with no skips.
The existing Vite chunk-size and ASP.NET test-host deprecation warnings remain. Focused daily
builder tests passed; full verification covers reader, schema, scripted conversation and two-user
isolation evidence. Updated existing compaction assertions to the new daily preload. Final diff
and documentation links reviewed; no database or existing public contract changes.
