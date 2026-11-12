# Calendar-based recurring tasks implementation plan

**Status:** Active — stage 1 (#89) merged; stage 2 (#90) implemented in this change; stages 3–8 remain unimplemented.\
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

## Stage 2 reviewed implementation plan

Outcome: persist tenant-bound series and terminal facts, with atomic occurrence actions and
restart-safe expiry/administrative closure, while public creation remains disabled.

| Acceptance | Implementation and verification |
| --- | --- |
| Upgrade existing tasks unchanged; DateOnly/UTC round trips | Nullable immutable schedule on TaskItem; operational cursor/calendar key and revision; CLI AppDbContext migration; upgrade/raw-storage tests |
| Unique facts and deterministic completion/expiry/correction races | Composite-key fact rows, status/timestamp check constraint; non-deferred SQLite transactions behind an Application unit-of-work port; concurrent independent-connection tests |
| Both missed policies and downtime catch-up | Application lifecycle service using the existing calculator and one calendar snapshot; bounded atomic batches with persisted prefix checkpoints; policy, restart, preference-change and full-period expiry tests |
| Bounded closure without unresolved work; no extra completion | Administrative recurring parent closure with no CompletedAt; end-date and complete eligible-prefix checks; ordinary completion regression tests |
| Eligibility, history and isolation | Check current task/list in the transaction; closed-series skipped correction and retry support; authenticated two-user tests; trash/restore and cascade tests |
| Reminder-independent wiring; read-only queries | Dedicated periodic web worker over active central accounts using existing trusted session scopes, plus a fixed-tenant stdio worker; lifecycle commands catch up explicitly; host tests with reminders disabled and read-only calendar tests |

Impact: Domain (TaskItem invariants/checkpoints); Application (transaction port and lifecycle);
Infrastructure (schedule mapping, fact rows/store, migration and fixed-tenant worker); Web and
stdio (DI/background wiring only); matching Domain/Application/Infrastructure/Web tests; reference,
schema and accepted ADR. HTTP/MCP schemas, Console chat, frontend, reports and history-query
implementation are not applicable; console delegates planning persistence to stdio.

Implementation order: domain administrative close/reopen guard, Application transaction/lifecycle,
Infrastructure mapping/store and generated migration, host wiring, focused tests, docs, full verify.
Reuse TaskGoalLinkRepository's non-deferred write transaction and WeeklyReminderHost's trusted
per-user session scope. Do not hook reconciliation into reads or initialization.

Review completed before feature code: all acceptance mapped; business policy remains inward;
planning tables only in AppDbContext; no tenant selector or public creation. Checkpoints commit
with facts and reset their scan on calendar-key changes without deleting history. Batches bound
transaction size, not the reachable history. Retain policy stores no pending rows. Trashing a
recurring parent administratively ends it; restore preserves that closure, so no inactive-gap
obligations can appear. Archiving a list hides work but does not suspend its calendar schedule;
background expiry continues, while user actions require an available list. Permanent task/list
deletion cascades facts. Recurring stale detached writes use an operational concurrency revision.
Successor mutation/resume and occurrence history reads remain stages 3/4. The overall plan stays
active through stage 8.

Completed-diff review: route existing TaskItemService completion and Trash through the shared
lifecycle (including catch-up before stopping); keep Trash available in archived lists. Cover
this with archived/non-archived regression cases and foreign-tenant rejection.

Validation: setup, matching focused projects, upgrade/concurrency/two-user worker tests, then
`./scripts/verify.sh --all`; inspect generated SQL/snapshot, complete diff and documentation links.

## Progress and evidence

### Stage 2 evidence

- Intake: #90/#84 and merged stage-1 contracts reviewed; clean worktree, master fast-forwarded
  to `6c65d90`; branch `feature/recurrence-persistence`.
- Distinct plan review completed before implementation; completed-diff review covers existing
  stored-task completion/Trash, archived-list behavior and recurrence revision protection.
- CLI generated `20261112215654_AddRecurringTaskLifecycle` for AppDbContext; inspected migration
  and snapshot. Upgrade test compares every pre-existing ordinary task column before/after.
- Focused new cases: Domain 4; Application 3; Infrastructure 42; Web 3. Evidence includes independent
  connection races, first-timestamp preservation, rollback/resume after committed batches, both
  policies, DST expiry, calendar changes, closed/out-of-bounds history corrections, Trash/cascades,
  unauthenticated/foreign-task rejection, disabled reminders, independent repeated tenant ticks
  and isolation of a broken account database.
- `./scripts/setup.sh`: passed. Final frontend lint/build and all 1,074 backend cases passed
  (Domain 62, Application 437, Infrastructure 344, MCP 95, Web 136).
- Final `./scripts/verify.sh --all`: passed frontend lint/build, all 1,074 backend cases and
  all 32 browser E2E cases after review refinements. Existing frontend chunk-size and ASP.NET
  test-host deprecation warnings remain. No live-model evaluation; existing scripted provider only.
- Documentation relative links and final whitespace validation passed.
- Updated schema/current recurrence references, export README and accepted ADR-0037; the overall
  plan stays active through stage 8. No public activation, successor commands or history queries.

### Stage 1 evidence

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
