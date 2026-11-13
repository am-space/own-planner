# Calendar-based recurring tasks implementation plan

**Status:** Active — stages 1–3 (#89–#91) merged; stage 4 (#92) implemented in this change; stages 5–8 remain unimplemented.\
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

PR #98 revision-failure review plan: snapshot each affected recurrence revision before advancing
it, restore the snapshot on any synchronous/asynchronous save failure, and rethrow the original
error. Extend regression coverage to repeated retries of the same detached task and retries in
the same context, plus cancellation and a recoverable failed write. Review: scoped to Infrastructure
and its tests; no schema, tenant selection or external contract changes. Verify the regression
fails before the fix, then run focused tests and full verification before pushing to the PR.

Validation: setup, matching focused projects, upgrade/concurrency/two-user worker tests, then
`./scripts/verify.sh --all`; inspect generated SQL/snapshot, complete diff and documentation links.

## Progress and evidence

### Stage 4 reviewed implementation plan

Outcome: provide tenant-bound, read-only mixed work-item pages and independent fact history,
with exact counts and full access to retained obligations across pages.

| Acceptance | Implementation and verification |
| --- | --- |
| Deterministic mixed pages and exact identities/counts | Application projection merges ordered ordinary/occurrence streams, filters before counting/paging, and counts distinct ordinary tasks/series separately; mixed, tie, filter and page-union tests |
| Retained older work remains reachable without pending storage | Explicit IncludeRetainedBeforeStart scans each retain series from its original start, subtracting all facts, without a lookback cap; lazy merge bounds generated-row memory; multi-decade paged regression and storage checks |
| Preserved facts outside generation bounds | Independent scheduled-date and CompletedAt history selections, never regenerated from rules; shortened/closed predecessor and early/late completion tests |
| Active eligibility versus historical access | Active planning excludes closed parents, Trash and archived/missing lists; history includes closed/predecessor facts but keeps the same Trash/list restrictions; eligibility matrix |
| Explicit bounds and disclosed paging | Required half-open ranges, maximum 3,660 date days / 3,660 elapsed days for instant history, page limit 100, nonnegative offset; exact total and HasMore, no hidden truncation; validation/empty/end-page tests |
| Tenant and snapshot consistency | Immutable source/fact snapshots under one tenant-local transaction and one resolved calendar per call; no reconciliation/save; independent-connection, calendar/DST/fallback and authenticated two-user tests |

Impact: Application (query contracts, projection/history services, read snapshot port); Infrastructure
(tenant-bound snapshot adapter); Web/stdio (internal DI only); Application/Infrastructure/Web tests;
reference/index and accepted ADR. Domain, migrations, TaskItemRepository legacy contracts, report
presentation/input wiring, MCP schemas, Console behavior and frontend are unchanged. Reports consume
these new inputs in #93; public creation/exposure remains gated through later stages.

Order: immutable source/query/page models and validated Application readers; Infrastructure snapshot
adapter; focused projection/history/storage tests; host wiring/isolation; reference/ADR; full gate.
Reuse RecurrenceCalculator, RecurrenceCalendar, PlanningCalendar and existing transactional readers.

Distinct plan review completed before feature implementation: all six acceptance items map above;
business rules stay in Application and persistence remains tenant-factory-bound. Existing history
methods remain supported; add a richer query page carrying selection/calendar/period/source metadata.
Ordinary selection is explicit (all open, focus, deadline, or either); focus is a calendar date and
deadlines are converted through the resolved timezone solely for selection/order. Ordinary tasks
appear once. Occurrence rows use only their original date and composite identity; parent focus and
deadline appear once in the page's distinct source metadata, not as occurrence deadlines. Active
planning defaults to pending work; explicit status filters can expose effective terminal statuses
within the window. Historical reads return recorded facts only, including advance facts outside
retired bounds. Before-window retention adds unresolved retain-policy work only. Ordering is work
date (undated last), task ID, then kind. Exact counts require scanning all matches; generated rows
are merged lazily rather than accumulated. Pages are consistent per call, not frozen across later
mutations; returning calendar metadata makes that scope explicit. No tenant selector, mutation,
external schema change, dependency or unrelated cleanup. Complete calendar periods must remain
representable under existing date-limit rules. The overall #84 plan stays active through #96.

### Stage 4 evidence

PR #100 task-filter review plan: narrow the loaded sources by optional TaskId before deriving the
retained fact range or creating occurrence streams. Keep the combined row filters, exact counts,
ordering and pagination unchanged. Add Application regressions with an unrelated decades-old daily
series and selected recurring, ordinary and unknown IDs, across consecutive pages. Verify only the
selected schedule is enumerated and the fact range starts at its own retained boundary (or the
requested window for ordinary/unknown IDs); unfiltered queries still include all sources. Review:
limited to Application and its tests plus read-contract documentation, with no schema, tenant,
transport or host changes. Run regressions before/after the fix, then full verification before
pushing and replying to the authorized review thread.

- Intake: #92/#84, merged predecessors and ADRs reviewed; clean branch
  `feature/recurrence-planning-queries` created from merged #99 (`efc1f75`). No issue/project state edits.
- Application implements mixed work and rich/fact-only history reads over an immutable snapshot port.
  Generated work is lazily merged before filtering/counting/paging; metadata contains one parent
  deadline per distinct source. Retained prefixes have no lookback cap or pending storage.
- Infrastructure uses the authenticated tenant factory and one transaction without tracking/saving.
  No migration, legacy TaskItemRepository, report, public MCP/HTTP or frontend contract changes.
- 42 added backend cases: Application 16 validation and 4 task-filter cases, Infrastructure 21 projection/history
  cases, Web 1 authenticated two-user case. Evidence includes mixed/tied pages and page unions,
  exact source/work counts, combined filters, 36 years of retained daily obligations, both policies,
  Sunday current/next coexistence, monthly clamping, full periods, source deadline/focus separation,
  early/late completion, scheduled versus activity selection, DST 167-hour weeks / 25-hour days,
  half-open instant boundaries, terminal-fact precedence, out-of-rule/out-of-bounds/empty-retired
  history, closure/Trash/archive access, extreme dates, read-only checkpoints/facts and a concurrent
  writer blocked while source/fact reads share a snapshot. Each query resolves one calendar.
- Distinct completed-diff review checked identities, ordering, filter/count/page order, source versus
  work counts, null/invalid selections, cancellation, tenant-factory use, lifetime/disposal, no writes,
  public activation gates and unchanged legacy contracts. Reference/index and accepted ADR-0039
  describe actual behavior and limits; overall #84 plan remains active through #96.
- Final contract review added a regression that failed because SQLite materializes legacy deadlines
  with DateTimeKind.Unspecified. The new source adapter now restores the UTC marker, preserving the
  instant without changing legacy persistence/DTO behavior; the regression passed after the fix.
- PR #100 regressions: recurring, ordinary and unknown task-ID cases failed before the fix because
  unrelated 1990 history widened their fact ranges; the unfiltered control passed. All four pass
  after narrowing sources, checking both fact bounds and calculator calls across consecutive pages.
- Focused checks passed: Application 20, Infrastructure 21 and Web recurrence host 5 cases.
  Final `./scripts/verify.sh --all` after the UTC-marker and task-ID review refinements passed frontend lint/build,
  all 1,158 backend cases (Domain 65, Application 459, Infrastructure 401, MCP 95, Web 138) and all
  32 browser E2E cases. Existing frontend chunk-size and ASP.NET test-host deprecation warnings
  remain. Dependencies were already prepared and unchanged. No live-model evaluation; browser
  checks use the existing scripted provider. Documentation links and staged whitespace checks passed.

### Stage 3 reviewed implementation plan

Outcome: change or resume a schedule through one linked successor without rewriting history,
duplicating successors on retries, or fabricating obligations during a break.

| Acceptance | Change and verification |
| --- | --- |
| Old Friday remains actionable after a Monday successor | Shorten only the predecessor end, retaining rule/start/policy and full-period expiry; both-policy lifecycle integration tests |
| Explicit future-fact choices preserve identities/timestamps | Read-only preview returns affected terminal facts, requested schedule and a later-start alternative; apply hashes/rechecks the parent, request and facts under the write lock; both resolutions and stale-preview tests |
| Atomic, retry-safe replacement/resumption | Tenant-local request ledger, unique link/request indexes, transactionally persisted cutoff/link/task/result; independent-connection retry/race and rollback tests |
| Identifiable chains, no earlier-bound extension | Nullable soft predecessor/successor task IDs; one successor per parent; chain, earlier-end, detached-write and migration tests |
| Resume completed series, no break work | Explicit replace/resume intent; resume keeps old completion, reanchors copied/new rule and omits an inherited end before the selected start; explicit/automatic close, trash/restore and history-correction tests |
| Closure follows the predecessor's own conditions | Existing reconciliation against shortened generation bounds, with unchanged full week/month expiry; empty retired ranges and retained/skip closure tests |

Impact: Domain (end-only shortening, explicit empty retired intervals, linked successor creation);
Application (preview/apply contracts and orchestration, snapshot/transaction port extensions);
Infrastructure (per-user links, request ledger/store and CLI migration); Web/stdio (DI only);
Domain/Application/Infrastructure tests and tenant-bound Web tests; schema/reference/ADR/index.
HTTP/MCP schemas, chat prompts/permissions, frontend, reports and public creation remain excluded.

Order: Domain invariants, Application contracts/service, Infrastructure persistence and generated
AppDbContext migration, host DI, focused tests, reference/ADR, full verification and final diff review.
Reuse the stage-2 non-deferred SQLite transaction, immutable recurrence rules and lifecycle service.

Reviewed before feature code: all criteria map above; no tenant selector or new external transport.
Preview takes a consistent read snapshot and never reconciles or saves. Affected facts include all
statuses on/after the requested start; any overlap requires an explicit resolution. Moving later
means after all affected dates, reanchors the proposed schedule, and is unavailable if no valid
date/end remains. Apply checks an opaque SHA-256 snapshot token inside the same transaction as
the link/cutoff/task/ledger writes; changed facts or parent metadata require a fresh preview.
Request-key reuse with changed inputs fails; a matching retry returns the original IDs even after
later chain changes. Links are soft IDs so deletion cannot cascade into another series; request
records are removed with either participating task. New schedule construction remains strict;
only end-shortening can create an empty retired range. DateOnly.MinValue cannot be a successor
start because its previous day is unrepresentable. No dependency or context-resolution changes.
The overall #84 plan remains active through stage 8 rather than being archived with stage 3.

### Stage 3 evidence

- Intake: #91/#84 reviewed, #90 merged at `d1a3522`; clean branch `feature/recurrence-successors`
  created from origin/master. No unresolved contract choices or dependency changes.
- Domain end-only shortening and explicit empty retired intervals preserve creation validation.
  Final review also guards ConfigureRecurrence against receiving a previously retired empty schedule.
- Application preview includes original facts, both successor schedules and predecessor boundaries;
  apply revalidates parent/facts and persists a request fingerprint/result atomically. Retry keys
  are tenant-local, exact-input and stable through further chain changes.
- CLI generated `20261113021538_AddRecurringSuccessors` for AppDbContext; inspected migration
  and snapshot. An upgrade regression preserves an existing recurrence, revision and completed fact.
- 36 added backend cases: Domain 3, Application 2, Infrastructure 30, Web 1. Evidence covers
  both choices and policies, stale metadata/facts, independent-connection matching/different-key
  races, failure after SQL writes before commit, preserved completion/cancellation identity,
  chain changes, explicit/automatic/trash-restored resumption, empty breaks, mid-week/month expiry,
  no earlier-end extension, repeated stale detached writes, read-only previews and two-user isolation.
- Schema/reference/index and accepted ADR-0038 describe the implementation. Existing ADRs remain
  historical records; the overall #84 plan stays active through stage 8.
- Final `./scripts/verify.sh --all` after the creation-validation review refinement: frontend
  lint/build, all 1,116 backend cases (Domain 65, Application 439, Infrastructure 380, MCP 95,
  Web 137) and all 32 browser E2E cases passed. Existing frontend chunk-size and ASP.NET test-host
  deprecation warnings only; no live-model evaluation. Workspace dependencies were already prepared
  by stage 2 and did not change. Documentation links and staged whitespace validation passed.

### Stage 2 evidence

- Intake: #90/#84 and merged stage-1 contracts reviewed; clean worktree, master fast-forwarded
  to `6c65d90`; branch `feature/recurrence-persistence`.
- Distinct plan review completed before implementation; completed-diff review covers existing
  stored-task completion/Trash, archived-list behavior and recurrence revision protection.
- CLI generated `20261112215654_AddRecurringTaskLifecycle` for AppDbContext; inspected migration
  and snapshot. Upgrade test compares every pre-existing ordinary task column before/after.
- Focused new cases: Domain 4; Application 3; Infrastructure 48; Web 3. Evidence includes independent
  connection races, first-timestamp preservation, rollback/resume after committed batches, both
  policies, DST expiry, calendar changes, closed/out-of-bounds history corrections, Trash/cascades,
  unauthenticated/foreign-task rejection, disabled reminders, independent repeated tenant ticks
  and isolation of a broken account database.
- PR #98 retry regression failed before the fix for both sync/async saves: the same stale detached
  instance could overwrite reconciled state on its second attempt. After the fix, three retries
  remain rejected, including reused contexts; cancellation and foreign-key failure restore the
  token and permit a valid repaired retry with exactly one revision increment.
- `./scripts/setup.sh`: passed. Final frontend lint/build and all 1,080 backend cases passed
  (Domain 62, Application 437, Infrastructure 350, MCP 95, Web 136).
- Final `./scripts/verify.sh --all`: passed frontend lint/build, all 1,080 backend cases and
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
