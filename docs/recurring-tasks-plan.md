# Calendar-based recurring tasks implementation plan

**Status:** Active — stage 1 (#89) implemented in this change; stages 2–8 remain unimplemented.\
**Contract:** [#84](https://github.com/am-space/own-planner/issues/84), including the November 12 decisions.\
**Outcome:** Represent each recurring series with one task, compute its calendar occurrences, and
preserve occurrence facts and predecessor history across schedule changes.

## Staged delivery

| Stage | Scope | Verification / dependency |
| --- | --- | --- |
| [#89](https://github.com/am-space/own-planner/issues/89) | Immutable contracts, bounded calendar engine, engineering decisions | Domain/Application tests; no persistence or public activation |
| [#90](https://github.com/am-space/own-planner/issues/90) | Per-user persistence, atomic fact actions, expiry and closure | CLI-generated AppDbContext migration, lifecycle/concurrency/isolation tests; after #89 |
| [#91](https://github.com/am-space/own-planner/issues/91) | Linked replacement/resumption, conflict preview/apply | Atomicity, retry and stale-conflict tests; after #90 |
| [#92](https://github.com/am-space/own-planner/issues/92) | Unified planning projection and history | Exact mixed counts/pages and retained obligations; after #91 |
| [#93](https://github.com/am-space/own-planner/issues/93) | Planning/reflection/strategic reports | Scheduled-date versus completion-time evidence; after #92 |
| [#94](https://github.com/am-space/own-planner/issues/94) | Weekly Review queries and guarded occurrence actions | Frozen targeting, counts, ordinary-task compatibility; after #93 |
| [#95](https://github.com/am-space/own-planner/issues/95) | Shared MCP handlers, hosts and chat permissions | Direct/HTTP/stdio schemas, scoped scripted conversations; after #94 |
| [#96](https://github.com/am-space/own-planner/issues/96) | One-row-per-series UI, references, final integration | Browser, migration, export/recovery, isolation and full verification; after #95 |

Each stage carries its own tests. Public series creation remains disabled until lifecycle safeguards,
reports and Weekly Review are ready. Keep #84 open until all stages and its overall acceptance
criteria pass. Publication and deployment require separate authorization.

## Stage 1 impact map

| Layer / path | Change |
| --- | --- |
| Domain | Validated recurrence rule/schedule, policy, occurrence identity/fact and series snapshot; no TaskItem mapping changes |
| Application | Pure bounded recurrence calculator, shared-calendar period/expiry helpers and documented lifecycle port |
| Infrastructure / migrations | Not applicable; no database or repository implementation changes |
| Web API / MCP / console / stdio | Not applicable; no registration, external schema or creation path |
| Frontend | Not applicable |
| Tests | Domain invariants and Application arithmetic/calendar tests alongside the code |
| Documentation | This active #84 plan, recurrence reference, stage-1 ADR and index/calendar links |

## Stage 1 acceptance mapping

| Acceptance | Change and evidence |
| --- | --- |
| Weekly/monthly intervals, weekdays, leap years, collisions, invalid rules and boundaries | Validated immutable Domain types and lazy bounded Application calculator; rule and calculator tests |
| Current and next periods coexist; Sunday does not expire this week | Shared-calendar current-plus-next range; simultaneous current/next query and expiry tests |
| Configured week starts and timezone/DST expiry without date shifts | CalendarRules-based local period boundaries and UTC expiry; offset, DST gap/overlap and exact-boundary tests |
| Replacement, old obligations, facts, resumption gaps and automatic closure examples | Executable contract examples where foundations apply, plus explicit reference scenarios for later transactional stages |
| Domain/Application pass; Domain has no EF and ordinary tasks retain behavior | Existing project dependencies and TaskItem remain unchanged; focused projects and full repository verification |

## Implementation order and reviewed decisions

1. Reuse the immutable value/record conventions and existing TaskItem tests; retain TaskItem and
   all existing external contracts unchanged.
2. Add immutable weekly/monthly rules and series/fact contracts in Domain. Copy and normalize
   selected days, reject invalid enums/empty selections/nonpositive intervals, and require UTC
   only for completion instants. Occurrence dates stay DateOnly.
3. Implement a pure Application calculator that seeks directly to the first relevant active
   week/month and lazily yields distinct ordered dates in an explicit half-open range. The series
   start is its interval anchor; its optional end is inclusive. No time/clock/store access.
4. Reuse CalendarRules for planning and expiry with a single resolved PlanningPeriod snapshot.
   Weekly rule alignment captures the configured week start when the rule is created; later
   preference changes do not rewrite scheduled dates. Today, display periods and expiry use the
   current shared calendar. Persisted facts are immutable history except explicit corrections.
   Preference changes apply prospectively to unresolved periods, never by deleting/recomputing facts.
5. Document parent FocusAt/DueAt, retained-obligation pagination, explicit write reconciliation,
   successor conflict/retry contracts and activation gates before later migrations.
6. Add tests, reference/ADR, run focused projects, then `./scripts/verify.sh --all` after setup.

Plan review completed before feature code: every #89 acceptance item is mapped above; dependencies
flow inward; no tenant selector, EF, transport, credentials, logs or unrelated cleanup is introduced.
The rule's captured alignment is distinct from the current user's reporting calendar. Transactional
operations are specified rather than claimed as implemented. The overall #84 plan remains active
through stage 8; the stage-1 ADR records only foundations actually delivered.

## Engineering boundaries and risks

- A pure calculator does not decide action eligibility or mutate skipped facts/parent completion.
  Stage 2 owns lifecycle validation and atomic writes; stages 3–6 must reuse those operations.
- Reads may project effective skipped status against one calendar snapshot but must never persist
  skipped facts, advance checkpoints, close series or open review state. Catch-up is an explicit
  tenant-bound lifecycle operation, independent of enabled reminders and process initialization.
- Retained obligations need a complete, paged query separate from a future range; the complete
  historical prefix must remain reachable. Stage 4 must combine projections before count/paging.
- Later writes must preserve original occurrence identity and first successful CompletedAt,
  including facts outside shortened schedule bounds. Generation rules cannot reconstruct history.
- A shortened predecessor may have no eligible dates (cutoff before original start). Creation
  validates normal start/end; stage 3 must support an empty retired generation interval explicitly.
- Public activation before reports/review are ready would hide work or miscount parent closure;
  no public creation or service registration is added in stage 1.
- Reminders, time-of-day recurrence, occurrence overrides, completion-relative schedules and a
  calendar/expanded-occurrence UI remain excluded.

## Progress and evidence

- Intake: #84 and #89–#96 reviewed; worktree clean and local master matched origin/master.
- Branch: `feature/recurrence-calendar-engine`.
- Plan and implementation review: complete. Stage 1 supplies immutable contracts, the bounded
  calculator, shared-calendar period helpers, future lifecycle/history ports and ADR-0036.
- `./scripts/setup.sh`: passed; no dependency or source version changes.
- `./scripts/verify.sh --all`: passed frontend lint/build, all backend suites and 32 browser E2E
  tests. Existing frontend chunk-size and ASP.NET test-host deprecation warnings remain.
- Final focused projects after review refinements: Domain 58 passed; Application 434 passed.
- Final `./scripts/verify.sh --backend` after review refinements: all 1,022 backend cases passed.
- Documentation relative links and staged whitespace validation checked.
- No live-model evaluation; browser/chat tests use the existing deterministic scripted provider.
- The overall plan remains active through #96, which will archive it with the completed feature ADRs.
