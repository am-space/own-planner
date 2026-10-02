> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0029: Shared user-calendar periods](../adr/0029-shared-user-calendar.md).
> Details below may not reflect later refinements made during review.

# Shared user-calendar rules

Status: Implemented — reviewed and verified

Canonical issue: [#75](https://github.com/am-space/own-planner/issues/75).

## Outcome

General, Week Planning, Day Work and Reflection resolve named periods against the current clock and
the user's explicit timezone/week start, while legacy UTC requests and weekly-review state remain compatible.

## Reviewed decisions and assumptions

- Reuse the read-only weekly-review preferences lookup even when reminders are disabled. A missing
  timezone uses UTC explicitly, with a returned fallback explanation; retain the configured week start
  (unconfigured default Monday). No browser/server timezone inference and no settings writes.
- Application owns a pure period resolver and a tenant-bound preferences/clock service. Extract common
  week-start and local-boundary conversion from weekly review, retaining its targeting and frozen boundaries.
- Named periods: today, thisWeek, remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays, lastSevenDays.
  The last is a rolling 168-hour instant range ending now, with focus membership based on the local
  dates touched by that range. Calendar windows use local midnight, inclusive start/exclusive end.
- Add optional calendarPeriod to shared reporting/focus tools. Omission preserves existing UTC
  behavior; reject combinations with legacy date/range overrides instead of silently choosing one.
- General retains every legacy UTC field and adds an explicitly separate calendar summary for today
  and the selected calendar week. #74's attention grouping, reason deduplication and drill-down remain separate.
- Mode preloads explicitly select local today/current week/previous complete week. A small read-only
  calendar context refreshes each turn; instructions require fresh named-period report reads for new
  requests and distinguish rolling periods. No natural-language date parser or new mode.

## Acceptance mapping

| Requirement | Change and evidence |
| --- | --- |
| Shared preferences, disabled reminders, missing preferences | Application resolver/service; fixed-clock and real SQLite preference tests; no preference/review rows written |
| Calendar week vs rolling period; last week and last day | Resolver tests for all periods, Sunday/Monday starts, month/year edges; review regression suite |
| Focus dates vs timestamp instants | Separate date and UTC boundaries in report readers; boundary membership tests across positive/negative offsets |
| DST gap/overlap and half-open ranges | Shared conversion tests including midnight gaps/overlaps and 167/169-hour weeks |
| General today/week plus compatible rolling fields | Additive calendar summary; legacy report tests and local summary tests |
| Week Planning windows and matching default | calendarPeriod options, preload arguments, prompt/orchestration and report tests |
| Day Work local initial date and refresh | Additive today selection on focus tool, preload arguments and cross-midnight tests; no deadline expansion |
| Reflection previous complete week and explicit rolling range | Additive named-period option; completion/focus boundary tests; historical limitations unchanged |
| Actual dates, timezone and freshness | Period metadata and per-turn context; adapter/orchestration tests across midnight and repeated reads |
| Tenant and shared transports | Existing bound factories plus two-user calendar/report isolation; MCP schemas/parsing and direct/stdio registration checks |
| Unchanged weekly review and reminders | Reuse only pure helpers and read-only preferences; existing review/reminder suites |

## Impact map and implementation order

1. Application (affected): shared calendar types/service, report options/metadata, mode preload arguments,
   per-turn calendar context and instructions. Domain: not applicable; no new entity rules.
2. Infrastructure (affected): existing tenant-bound readers select local date/UTC instant ranges;
   model adapter injects current request context. No database migration, new dependency or tenant resolver.
3. MCP (affected): additive options and one read-only calendar lookup; preserve old names, defaults,
   arguments and result fields. Web API routes/frontend: not applicable.
4. Web and stdio (affected): DI and shared tool registration. Console reuses MCP and PlanningService.
5. Tests: Application, Infrastructure, MCP and Web integration evidence as mapped above.
6. Docs: calendar reference, AI integration/weekly review references, accepted ADR and archived plan.

## Verification and review

Run focused calendar, report, tool, mode and adapter tests during implementation. Then run
`./scripts/verify.sh --all` (setup first if dependencies/workspace require it), inspect the complete diff,
check every acceptance row and `git diff --check`. Review confirmed inward dependencies, bounded
outputs, unchanged tenant selection, additive contracts and no unrelated cleanup before implementation.

Excluded: #74 attention presentation, deadline expansion in Day Work, new Reflection classifications
or capture permissions, scheduling/capacity, reminders and weekly-review lifecycle changes.

## Delivery evidence

- Application calendar and orchestration tests: 361 passed, including 27 additional cases.
- Infrastructure: 248 passed, including local report boundary tests and actual provider-request tests.
- MCP: 75 passed, including optional schema, fresh local date, parsing and compatibility coverage.
- Web: 130 passed, including two-user calendar/report isolation and direct tool schema/error checks.
- Domain: 39 passed. Existing weekly-review/reminder and goal-choice compaction suites remain green.
- `./scripts/verify.sh --all`: frontend lint/build, 853 backend tests and 26 browser E2E tests passed.
- Complete diff/acceptance review and `git diff --check` passed; reference links checked.
- No migration, dependency, HTTP route, UI or review-lifecycle changes. Existing Vite chunk warning.
- Natural-language selection is model behavior; tests use scripted provider responses, not live Gemini.
