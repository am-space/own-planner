# Recurring tasks and shared MCP operations

Stages 1–8 ([#89](https://github.com/am-space/own-planner/issues/89),
[#90](https://github.com/am-space/own-planner/issues/90),
[#91](https://github.com/am-space/own-planner/issues/91),
[#92](https://github.com/am-space/own-planner/issues/92),
[#93](https://github.com/am-space/own-planner/issues/93),
[#94](https://github.com/am-space/own-planner/issues/94),
[#95](https://github.com/am-space/own-planner/issues/95),
[#96](https://github.com/am-space/own-planner/issues/96)) provide recurrence types, a
deterministic calendar engine, per-user persistence, lifecycle reconciliation and linked successor
commands, unified planning reads, fact history, occurrence-aware reports and Weekly Review, and shared
public MCP operations with chat safety policies, and one-row-per-series Planner presentation.
The [archived implementation plan](archive/recurring-tasks-plan.md) preserves the eight-stage work
for [#84](https://github.com/am-space/own-planner/issues/84); the
[acceptance evidence map](recurring-tasks-verification.md) identifies verification for each requirement.

## Implemented types and calculation

| Type | Contract |
| --- | --- |
| `RecurrenceRule` | Immutable weekly/monthly frequency, positive interval and normalized nonempty day selection; weekly rule captures week start |
| `RecurrenceSchedule` | Rule, earliest eligible `StartDate`, optional inclusive `EndDate`, explicit `MissedOccurrencePolicy` |
| `RecurringSeries` | Task ID, schedule, parent completion flag, optional predecessor/successor task IDs; a read snapshot, not a new persisted entity |
| `TaskOccurrenceId` | Original `(TaskId, OccurrenceDate)`; there is no separate persisted task ID per occurrence |
| `TaskOccurrenceFact` | Identity, completed/skipped/cancelled status, nullable actual completion instant; no pending status |
| `OccurrenceDateRange` | Explicit nonempty half-open `[StartDate, EndExclusiveDate)` range |
| `IRecurrenceCalculator` / `RecurrenceCalculator` | Pure lazy, ordered, distinct date generation within both query and series bounds |
| `RecurrenceCalendar` | Current-plus-next periods and full occurrence periods/expiry from a shared `PlanningPeriod` snapshot |
| `TaskItem.Recurrence` | Nullable immutable series schedule; ordinary tasks retain null and existing behavior |
| `IRecurringTaskLifecycle` / `RecurringTaskLifecycle` | Tenant-bound atomic fact/series actions and explicit reconciliation, registered in web and stdio |
| `IRecurringTaskStore` | Application write-transaction boundary, implemented by Infrastructure against a user-bound AppDbContext |
| `IRecurringTaskSuccessors` / `RecurringTaskSuccessors` | Internal read-only preview and atomic replacement/resumption with explicit overlap choices and retry keys; registered in web and stdio |
| `IPlanningWorkReader` / `PlanningWorkReader` | Mixed ordinary/occurrence pages, exact work and source counts, optional complete retained prefix |
| `ITaskOccurrenceHistory` / `TaskOccurrenceHistory` | Recorded facts by identity, scheduled date or actual completion instant, including preserved predecessor history |
| `IPlanningWorkStore` / `PlanningWorkStore` | Read-only immutable source/fact snapshots under one tenant-local transaction; no SaveChanges or reconciliation |

For weekly interval N, the week containing `StartDate` is week zero using the rule's captured
week start. Generate selected weekdays in weeks 0, N, 2N, etc., excluding dates before `StartDate`.
For monthly interval N, the start month is month zero. Generate selected days in months 0, N, 2N,
etc., again excluding earlier dates. Query boundaries never re-anchor the interval.

Each monthly selection clamps independently to that month's last date. Selections 29, 30, 31
produce one February 28 in a common year, one February 29 in a leap year, then March 29, 30, 31.
The stored selection is never replaced by February's result. Selecting all seven weekdays
expresses daily work in eligible weeks; there is no daily frequency or fixed-hour arithmetic.

Rules defensively copy day selections. Invalid/empty selections, undefined enums, nonpositive
intervals and backwards creation bounds are rejected. An end equal to the start permits a
single-day series. Generation seeks directly to the first relevant active week/month, so a
distant query does not walk the entire series history. The calculator does not load facts,
filter completed parents, apply a missed-work policy or silently truncate results. Callers own
those decisions and bounded payloads. Half-open DateOnly ranges cannot include DateOnly.MaxValue;
period helpers reject dates whose complete containing/following period exceeds DateOnly limits.

## Calendar, preferences and expiry

Resolve `IPlanningCalendar` once per operation to obtain the clock, today, timezone, current week
start and fallback explanation. Reuse that snapshot across projection and period evaluation.
Missing timezone follows [the shared user-calendar contract](user-calendar.md): disclosed UTC,
stored week start (Monday by default), no browser/server inference and no reminder opt-in required.

The captured weekly alignment keeps generated occurrence dates stable if preferences later change.
Changing reporting preferences does not edit the rule, restart its interval or move existing facts.
Today, reporting windows and expiry of unresolved occurrences use the current shared calendar.
For example, a Sunday occurrence from a Monday-aligned biweekly rule stays on its original Sunday
after changing the preference to Sunday-start weeks, while its current expiry period can change.
Recorded skipped/completed/cancelled facts are not retrospectively erased or reinterpreted.
Checkpoints carry the resolved timezone/week-start key. A changed key restarts scanning at the
original series start, preserving all recorded facts and evaluating unresolved dates without gaps. An
alignment change to the recurrence itself uses an explicit successor, not a preferences side effect.

An occurrence remains in its **full calendar week/month**, even if its scheduled date is past or
the series end is earlier than the period end. `PeriodContaining` uses `CalendarRules.WeekStart`
and `CalendarRules.ToUtc`; expiry begins exactly at the exclusive period-end UTC instant. Midnight
gaps advance to the first valid minute and overlaps select the earlier instant. A week may contain
167 or 169 hours. Recurrence and occurrence dates themselves are never converted to UTC instants.

On Sunday November 15, 2026 with Monday-start weeks, a Friday November 13 occurrence is still
actionable. A two-week range `[2026-11-09, 2026-11-23)` also shows November 20. Querying next week
does not expire this week. Monthly current-plus-next windows work the same way with month bounds.

## Facts and parent fields

`TaskOccurrenceFacts` has composite primary key `(TaskId, OccurrenceDate)`. DateOnly values use
`YYYY-MM-DD` and actual completion instants use UTC round-trip ISO timestamps ending in `Z`.
A database check constraint requires completed facts to have non-null `CompletedAt`, and skipped/
cancelled facts to have null. Undefined statuses are rejected. Pending dates have no stored row.

Friday November 13 completed Monday November 16 retains November 13 as `OccurrenceDate` and
Monday's actual instant as `CompletedAt`. Scheduled-period outcomes select by the former;
completion activity selects by the latter, with explicit labels and no double counting within
either view. A default completion uses the injected clock. An explicitly reported historical
instant is accepted only after calendar ambiguity is resolved and cannot be later than the operation's
clock snapshot. A future scheduled occurrence can be completed now, with the actual current instant.
Retries preserve the first successful timestamp; a skipped-to-completed correction updates the existing fact atomically.
Historical corrections never reopen a parent or resume a series.

Parent `FocusAt` remains a manual calendar focus on the stored series; it does not move or override
occurrence dates. Parent `DueAt` remains a distinct deadline instant on the series, never inferred
from an occurrence date and never multiplied into one deadline per occurrence. Unified reads distinguish
series-level metadata from occurrence work items and deduplicate source counts. Neither field is
copied automatically to a successor. Ordinary task fields, completion,
reopen and external DTOs remain compatible. A recurring parent's administrative close leaves its
`CompletedAt` null. Ordinary completion still records an actual timestamp.

## Implemented writes and lifecycle

`RecurringTaskLifecycle` implements `IRecurringTaskLifecycle` against the authenticated tenant's
`AppDbContext` through `RecurringTaskStore`. No operation accepts a user ID or database path.
A non-deferred SQLite write transaction serializes current parent/list eligibility, the fact
transition and the result against other connections/processes. User actions require a non-trashed
series in an existing, unarchived list. A new fact also requires an open parent and a generated
date; recorded skips may be corrected on closed series or outside current generation bounds.
Existing completed/cancelled facts cannot be overwritten by expiry. Cancellation is retry-safe;
cancelling completed/skipped work or completing cancelled work fails explicitly. Repeated completion
returns its first timestamp, including on a closed parent, subject to the same access restrictions.

Ending a series suppresses future occurrences and preserves history. It is distinct from completing
one occurrence. Parent completion, whether explicit or automatic, is administrative closure and
does not add a completed occurrence. A naturally bounded series closes automatically only after
its inclusive end has passed **and** every eligible date is resolved under its policy. Examples:

- With retain-each-obligation, Friday November 13 unfinished after a November 15 series end keeps
  the old series open until that occurrence is completed or cancelled.
- With skip-expired-periods, the same Friday remains open through its week. If the series ends
  Friday November 13, Saturday is still too early to skip it or close it with unresolved work.
  If all dates are already completed/cancelled, closure need not wait for period expiry.
- Once the full week ends, reconciliation may record the missing skipped fact and close that
  bounded series. This is not evidence the work was performed.
- An unbounded series never auto-closes merely because all past occurrences are resolved.
- A bound with zero eligible dates can close after its end; no completion fact is fabricated.

Reconciliation is a retry-safe explicit write operation, invoked before lifecycle actions and by
dedicated periodic hosts. Each transaction scans up to 256 prefix dates, followed by at most a
current-period check for closure, and commits its facts and exclusive-prefix checkpoint together.
More batches continue until caught up; there is
no historical lookback cap. Failure between batches resumes from the committed prefix next time.
Skip policy processes dates before the current full calendar period. Retain policy stores no
pending rows; once the end passes it checks the resolved prefix and stops at the first unresolved
date. Operational cursor/calendar key/revision belong on TaskItem, outside the fact payload.

The web worker ticks every minute over active central accounts, creates a trusted per-user session,
ensures migrations/seeding, then calls reconciliation every time. It needs no Telegram link or
reminder opt-in. A failed account does not prevent other accounts catching up; logs contain only
the failure type. The stdio worker processes its fixed tenant immediately after startup migrations
and every minute thereafter. Console uses that stdio host. Multiple hosts remain safe through the
database transactions. These workers do not add a public creation path.

Stored-task completion and Trash route recurring parents through shared termination/Trash and
catch up expired history before stopping; Trash remains available in archived lists. Recurring
reopen directs callers to explicit successor-based resume; ordinary reopen is unchanged.
Trashing a recurring task ends its series and retains facts; restore retains the closed parent,
so an inactive interval never produces new obligations. Archived lists hide work and prevent user
actions but do not suspend the calendar schedule: background expiry continues. Permanent task
deletion cascades facts. List deletion cascades active tasks/facts while trashed tasks/facts remain
recoverable under the existing original-list restore checks. A recurrence revision rejects stale
detached updates that could overwrite closure or checkpoint state; callers must refresh and retry.
Failed or cancelled saves restore the pre-save revision and its modification flag, so repeatedly
reattaching the same stale instance cannot acquire a newer token and bypass the conflict.

Reads, including calendar lookup, reports, preview and history, remain read-only. They may compute
effective expiry from one clock/calendar snapshot while a background writer is catching up, but
must not persist skips, advance checkpoints, close parents, mutate preferences or open review state.

## Implemented successors and resumption

Given requested start D, atomically shorten the old generation end to the earlier of its existing
end and D minus one day; never extend it. Preserve its original rule. Create one linked successor
starting on D, copying title, description, list, goal, importance and missed policy unless that
policy is explicitly changed. Do not copy `DueAt` or manual `FocusAt`. A cutoff before the old
start leaves an explicitly empty retired interval (`RecurrenceSchedule.IsEmpty`), with the original
start and an end before it. Only `ShortenBefore` can construct that retired range; new-series and
successor creation still reject backwards bounds. Storage round-trips the original JSON shape.
The old occurrence dates and facts always retain their old task ID.

For example, replacing a Friday schedule with Mondays starting November 16 leaves unfinished
November 13 work on the predecessor, independently of the successor's November 16 occurrence.
The old occurrence expires at its full original week end, never at the replacement cutoff.

Preview shows all affected terminal facts on/after the requested start, both proposed successor
schedules and their corresponding predecessor cutoffs. For an advance
completion on November 20 and requested start November 16, require an explicit choice: show a
later start beyond the affected completion dates, or keep November 16 and authorize successor
occurrences as separate work. Keep original identity, status and CompletedAt even if the fact
falls outside the old shortened bounds. Cancellation facts are preserved too; nothing transfers.

`PreviewAsync` takes a consistent database snapshot without saving or invoking reconciliation.
Its opaque SHA-256 token covers the parent revision, original schedule, copied metadata, request,
proposed schedule and ordered affected facts. `ApplyAsync` recomputes that token under the same
non-deferred SQLite transaction as cutoff/link/creation. Changed snapshots require a fresh preview.
Any affected facts, including cancellation/skips, require a `SuccessorConflictResolution`:

- `MoveStartAfterFacts` places the new anchor one day after the latest affected fact. The alternative
  is unavailable if that date cannot be represented or exceeds the proposed end.
- `KeepStartAsSeparateWork` retains the requested anchor and explicitly treats successor work
  independently. No completed/cancelled/skipped fact is created or transferred to the successor.

Apply requires a nonempty tenant-local GUID request key. `RecurringSuccessorOperations` stores the
successful request fingerprint and original source/successor IDs in the same transaction. An identical
retry returns those IDs, including after further chain changes; changing the request, preview token
or choice with the same key fails. Concurrent different keys cannot create sibling successors.
Nullable soft predecessor/successor task IDs identify the chain; change its tip for subsequent edits.
Deleting a series does not cascade into its neighbors; deleting either task removes its associated
request record. Retry guarantees apply while the participating tasks remain available.

Resumption uses the same successor mechanism for explicitly or automatically completed series.
For an old series ended November 15 and a successor beginning December 7, the intervening dates
produce no obligations or skipped facts. Keep the old parent completed, use the selected new start
as the interval anchor, and do not inherit an invalid past end. Recurring `taskitem_reopen` must
direct callers to explicit resume; ordinary reopen remains compatible. Future-fact conflicts and
retry handling follow the replacement contract. History correction never creates a successor.

`RecurringSuccessorRequest` distinguishes `Replace` (open parent) from `Resume` (completed parent).
Null rule/policy inherit. Null end inherits unless `ClearEndDate` is true; resume additionally drops
an inherited end before the new start. An explicitly supplied invalid end always fails. A replacement
past an inherited end must explicitly clear or replace that end. The chosen start reanchors the new
interval; the old rule/start/policy are unchanged. `DateOnly.MinValue` cannot be a successor start
because its preceding cutoff date is unrepresentable. Requests require a non-trashed chain tip in an
existing unarchived list, using the same tenant-bound factory as lifecycle actions.

Replacement itself neither administratively closes nor reopens the old parent. Existing periodic
reconciliation applies that parent's own shortened bounds and full original periods; unresolved
retained work keeps it open. A completed predecessor remains closed through resumption, so no
break-period obligations or skips are generated. Shared successor preview/apply tools expose this behavior; Planner details show linked series.

## Implemented planning and history reads

`IPlanningWorkReader.QueryAsync(PlanningWorkQuery)` is the shared internal input for later reports
and tools. It requires a nonempty half-open `OccurrenceDateRange` of at most 3,660 calendar days,
nonnegative offset and page limit 1–100. The default selects pending work, combining ordinary tasks
focused or due in the range with generated occurrences. Ordinary selection can instead be focus
only, deadline only, all open tasks (including undated), or none. Focus values are calendar dates;
deadline instants use the resolved timezone for range selection and ordering. A task selected by
both fields still appears once, ordered by its in-range focus date before its in-range deadline.
Deadline-only selection orders by deadline date; all-open tasks without an in-range field use
focus then deadline, with undated work last.

Rows have `TaskId`, kind, work date, optional original occurrence date/full period, effective status
and actual completion instant. Ordinary identity is its task ID; occurrence identity is the original
`(TaskId, OccurrenceDate)`. No occurrence task IDs or deadline instants are fabricated. Distinct
`Sources` metadata accompanies the returned page, carrying title, description, list/context/goal,
importance, parent state, schedule/policy, links, manual focus and parent UTC deadline once per source.
SQLite's missing DateTime.Kind on legacy deadlines is restored as UTC in this read model.
Those assignments are current metadata, not historical snapshots. A parent deadline does not become
one deadline per occurrence, and a recurring parent is never an additional ordinary work row.

The reader lazily merges source streams by work date, task ID and kind, with undated work last.
Task-ID selections narrow sources before retained-prefix calculation and occurrence enumeration,
so unrelated histories are not scanned for a targeted recurring, ordinary or unknown task.
Status, kind, task/list/context/goal, importance and case-insensitive title/description filters apply
to the combined stream before exact counting and paging. Null status selects all effective statuses;
the default is `Pending`. Facts take precedence over computed expiry. Unrecorded expired skip-policy
dates project `Skipped`; they are not written. `Counts` separates ordinary tasks, distinct series
and occurrences; `TaskCount` counts distinct sources, `WorkItemCount`/`TotalCount` counts work rows.
Every page includes exact totals, offset, limit and `HasMore`, with no hidden result truncation.

`IncludeRetainedBeforeStart` explicitly adds every unresolved retain-policy date before the window,
from the original series start through the window start, including bounded predecessors. All recorded
facts are subtracted from this prefix, regardless of the requested status filter. No lookback cap
drops older obligations. Generation still honors each series' own bounds and never materializes
pending rows. A priority queue holds one next generated row per source; exact counts scan every
eligible date, so very long retained histories cost CPU without accumulating generated rows in memory.
Skip-policy work before the requested window is not implicitly included.

`ITaskOccurrenceHistory` reads recorded facts independently of rules and generation bounds:

- `GetAsync` addresses the original composite identity, including dates outside retired bounds or
  no longer matching a rule.
- `QueryAsync` explicitly chooses `ScheduledDate` with a calendar range, or `CompletionTime` with
  UTC instant boundaries. Supplying both bases, missing/backwards/non-UTC bounds, or a selection
  exceeding 3,660 calendar days / 3,660 elapsed days respectively fails before database access.
- Scheduled history includes completed/skipped/cancelled facts, ordered by occurrence date then
  task ID. Completion activity includes only completed facts in `[StartUtc, EndExclusiveUtc)`,
  ordered by CompletedAt, task ID, then original date. Resolve local boundaries through the shared
  user calendar before supplying instants. Late/early completion never changes the original date.
- Rich history pages expose the selection basis/bounds, exact total, page metadata, resolved calendar,
  full scheduled periods and distinct current source metadata. The original `ListByScheduledDateAsync`
  and `ListByCompletionAsync` methods remain available as fact-only tuple adapters.

Active planning excludes completed parents, Trash and archived/missing lists. History includes
closed and predecessor series under the same Trash/list restrictions. It returns facts only; it
never reconstructs expected or skipped history and never counts administrative parent closure.
Facts outside generation bounds remain history rather than obligations to regenerate. Complete
weekly/monthly periods must be representable in DateOnly, as required by the existing period
helper; an unrepresentable period fails explicitly instead of silently dropping a row. Direct
fact lookup remains available at extreme dates, including DateOnly.MaxValue.

Each page resolves one `PlanningPeriod` clock/timezone/week-start/fallback snapshot and reads sources
and facts under one tenant-local transaction. There is no user/database selector. No query saves,
reconciles, advances checkpoints or creates review/preferences state. A subsequent page is a new
snapshot and can reflect concurrent writes; this is deterministic paging per call, not a frozen
multi-request export. Missing timezone uses the disclosed shared UTC fallback. Infrastructure loads
current source metadata and selected persisted facts in memory; only generated work streams are lazy.

## Integration boundary

Shared MCP creation/actions and occurrence drill-down are enabled in stage 7 after occurrence-aware
reports and Weekly Review. Existing `taskitem_list_*` and browser shapes retain their stored-task
meaning. Planner presentation is described below; the overall implementation plan is archived with stage 8.
Parent closure adds no work, and occurrence counts never imply effort or available capacity.

## Occurrence-aware reports (stage 5)

General, local General calendar/attention, Day Work, Week Planning, Reflection and strategic reports
include an additive `Work` block. Existing fields and GUID samples retain stored-task semantics;
use `Work` for mixed actionable work. General's original UTC block covers today through today+8
calendar dates and today's completion activity. `Calendar.Work` uses the requested local period.
Day/attention/weekly/reflection work uses the report's resolved window; strategic work covers the
next seven local calendar dates including today. Explicit legacy weekly/reflection overrides keep
UTC behavior. Rolling reflection's date selection includes touched dates while its activity keeps
the exact instant boundaries shown in the report.

| Section | Selection and units |
| --- | --- |
| `Pending` | Open ordinary work selected by focus or local deadline date; computed pending occurrences in the window, earlier unresolved occurrences in overlapping current periods, and the complete retained prefix |
| `Scheduled` | Original occurrence dates inside `ScheduledRange`, plus ordinary focus/deadline selection; calculated effective outcomes union independent recorded facts, with each fact taking precedence once |
| `CompletionActivity` | Ordinary completions plus occurrence facts whose actual `CompletedAt` lies in `[ActivityStartUtc, ActivityEndExclusiveUtc)` |

Ordinary selection and its representative date always use the original report range: an in-range
focus date takes precedence, otherwise an in-range local deadline date selects the task. Expanding
the occurrence window must not allow an older focus date to hide a deadline inside the report.

Sections overlap and must not be summed. `Counts` distinguishes ordinary tasks, distinct series,
occurrences, stored sources (`TaskCount`) and work items (`WorkItemCount`). Status counters describe
work items, including explicit `PendingOccurrenceCount`; `BeforeWindowCount` discloses backlog.
Each section and in-window day has exact totals and independent bounded samples. Weekly,
reflection and strategic reports also include goal/context groups; compact General, attention and
Day views omit these groups so workspace size cannot expand their payloads.
Daily summaries cover only dates inside the requested range, so decades of retained obligations
cannot expand the daily payload. Zero samples still return exact evidence and `Truncated=true`
when work was omitted. Samples do not establish effort, overload or execution priority.

A sample identifies an ordinary task by `TaskId` and an occurrence by `(TaskId, OccurrenceDate)`.
It carries the full occurrence period, effective status and actual completion timestamp. Titles
are clipped at 80 UTF-16 characters without splitting a surrogate pair; descriptions are absent.
Samples sort by selection date, task ID and original occurrence date. Source metadata is emitted
once per distinct sampled task in `Sources`; its real deadline is never copied into occurrence
rows or counted as a new commitment per occurrence.

A Friday occurrence completed Monday appears once in Friday's scheduled results and once in
Monday's completion activity. Early completions and preserved successor facts outside shortened
bounds follow the same rule, including after parent closure. A skipped-to-completed correction
replaces its current status in fresh metrics. Parent closure contributes no completed work,
including a parent with a manual focus date. Reflection's legacy completed/missed-focus counts
refer to ordinary tasks; creation/remaining-task counts still describe stored records.

Weekly no-focused-work signals take pending scheduled occurrences as concrete planning evidence.
Reflection no-completed-work signals include actual occurrence activity. Neither signal depends
on sampled rows. Strategic legacy structural observations continue counting each linked series
once; `Work` provides its separate occurrence evidence without changing #79's historical limits.

The shared report aggregator visits the stage-4 projection under the reader's existing tenant
transaction and resolved calendar. It does not reconcile or save. Current metadata/preferences are
not reconstructed historical assignments. Existing report tools and all six mode preloads return
the same additive block through in-process/HTTP/stdio serialization, with shared interpretation
guidance. No tool arguments or recurrence mutation permissions were added. Existing section/offset
paging still applies to the original stored-task sections; `Work` is a separate bounded overview,
with its own dates, sample limit and truncation. General public occurrence drill-down uses stage-7 `planning_work_query`; the existing-review drill-down is described below.


## Occurrence-aware Weekly Review (stage 6)

Weekly Review uses its frozen timezone, target weekday and week boundaries for occurrence dates
and expiry. Its pending selection spans full current weeks/months through the target week and the
complete unresolved retained prefix. Sunday can therefore review next Friday while the current
Friday remains independently actionable, including across month and DST boundaries.

The existing task page counts stored ordinary tasks/series once. Recurring source rows add
`IsRecurring`, `PendingOccurrenceCount` and `PlannedOccurrenceCount`. Pending target-week work for an
active goal does not hide earlier unresolved occurrences of the same parent. Real deadlines are
source-level commitments. Recurrence dates never imply an overdue deadline or capacity estimate.

`Report.Occurrences` has an independent first page (20 items), exact totals, distinct series and
carryover/planned counts, original `(TaskId, OccurrenceDate)` identities, full periods, revisions,
guard tokens and deduplicated source metadata. Use `weekly_review_occurrences` or authenticated
`GET /api/weekly-review/{id}/occurrences` with independent `offset`, `limit` (1–50) and optional
`taskId`. Supplying `taskId` plus `occurrenceDate=yyyy-MM-dd` reads one eligible pending occurrence or
recorded historical fact, including closed predecessors and dates outside shortened bounds. These
reads claim no guidance, change no preferences and never reconcile or write pending facts.

`Goals.Work` / each goal's `Work` reuse the report sections. `WorkShare` splits completed and planned
work between active goals and other work with explicit ordinary/series/occurrence counts; per-goal
`PlannedWork` does the same. Completion activity retains the existing rolling seven elapsed days,
including the report instant (`[now-7d, now+1tick)`); the stalled check uses fourteen days and existing
creation/resume grace. Original scheduled outcomes use the frozen target-week date range. A Friday
occurrence completed Monday counts in Monday activity and keeps Friday's scheduled identity.
Administrative parent closure never adds completion activity. Legacy completion counters/samples
refer to ordinary tasks; legacy open/planned counters describe stored sources. No-plan signals use
exact pending target-week occurrences independently of samples, while no-next-step still means no
eligible open task/series record. Current assignments are not reconstructed historical membership.

`weekly_review_apply` adds `completeOccurrence` and `cancelOccurrence`, requiring original
`occurrenceDate`, the existing `revision`, and a fresh `guardToken`. Completion defaults to the
injected clock; optional `completedAt` accepts an explicitly reported actual instant with Z/offset,
no later than now. Never derive it from the scheduled date. Other actions reject this argument. Eligibility, review state,
parent/list, schedule/revision and fact are revalidated together under one tenant write lock before
using the shared lifecycle transition. Stale data returns `applied=false`: refresh and resolve the
changed target before retrying. Recorded skipped history can be explicitly completed even on a
closed predecessor. This never reopens the parent or changes successor work. Ordinary review
completion/focus/Trash/deadline actions reject recurring parents. A guard never authorizes a
successor conflict choice; schedule changes keep their separate preview/apply contract. No
occurrence rescheduling or rule editing is introduced.

Browser/Telegram summaries use mixed work counts; browser lists remain one row per series, with
pending-occurrence annotations and deduplicated completed-source titles. Chat provides detailed
occurrence paging/actions. Reminder preferences and delivery/guidance claims are unchanged. See
[Weekly Review](weekly-review.md) and [ADR-0041](adr/0041-occurrence-aware-weekly-review.md).


## Shared MCP operations and chat (stage 7)

All tools below are implemented once in `RecurringTaskTools` and registered in the authenticated
in-process adapter, bearer-only HTTP MCP endpoint and stdio host. Console chat uses the stdio host.
No argument selects a tenant or database. New argument schemas come from the same SDK generator,
including nullable arrays/defaults, and results are normalized once with the shared MCP serializer.
Enums in these new results are named strings (`Weekly`, `Completed`, etc.), dates are `YYYY-MM-DD`,
and actual completion instants are UTC ISO timestamps ending in `Z`. Existing tool shapes retain
their prior serialization. Missing nullable fields remain omitted under SDK defaults.

| Tool | Contract |
| --- | --- |
| `recurring_task_create` | Required title/list/start/frequency/missedPolicy; insert one configured parent. Optional description, importance, goal and inclusive end. No generated child tasks or inferred deadlines |
| `recurring_task_configure` | Atomically configure an existing active ordinary task; reject existing series, closed/Trash/unavailable-list targets |
| `recurring_task_get` | Parent fields, schedule and predecessor/successor links, including closed history; no reconciliation |
| `planning_work_query` | Required bounded startDate/endExclusiveDate; shared mixed projection, filters, exact counts and offset/limit (1–100), optional full retained prefix |
| `task_occurrence_get` | Recorded fact by original taskId/occurrenceDate; a missing fact is not inferred completed or eligible |
| `task_occurrence_history` | Explicit ScheduledDate or CompletionTime basis, matching bounded date or instant boundaries, exact pages and metadata |
| `task_occurrence_complete` | Composite identity; optional explicitly supplied actual completedAt with offset, not future; retry preserves first timestamp, explicit skipped correction allowed on closed/predecessor series |
| `task_occurrence_cancel` | Composite identity; only eligible unresolved work, no completion timestamp or series termination |
| `recurring_task_successor_preview` | Read-only Replace/Resume proposal, original affected facts, both possible schedules and snapshot token |
| `recurring_task_successor_apply` | Same preview inputs/token, requestId and any explicitly chosen resolution; atomic stale checks, retry-safe linked successor |
| `recurring_task_end` | Explicit whole-series administrative termination, no additional completed work |

Creation/configuration requires `missedPolicy=SkipExpiredPeriods` or `RetainEachObligation`. Chat
asks when that choice is missing, never inferring policy from the title. `frequency=Weekly` needs
weekday names; `Monthly` needs monthDays. Interval defaults to 1. New parents capture week start
from shared calendar preferences. A changed Weekly successor rule additionally requires explicit
`weekStart` (0–6) from `calendar_period_get`, reused identically on apply/retry. This prevents a
preference edit from changing the request after preview or a successful retry. An omitted successor
rule inherits the predecessor's immutable alignment. Partial rule edits without frequency are rejected.

Strict date parsing rejects timestamps for occurrence dates. Supplied actual/history instants require
an ISO date/time with seconds and explicit Z or offset. Ambiguous local/DST times are clarified before
calling tools; a scheduled date never supplies an actual completion timestamp. Expected validation,
unavailable-target and stale-choice errors return an `error` result; cancellation propagates.

Successor previews show all future affected facts, including cancellation and preserved out-of-bounds
history. No conflict means an otherwise authorized replacement can apply directly. Any affected fact
requires the user's explicit `MoveStartAfterFacts` or `KeepStartAsSeparateWork` choice after inspecting
facts and schedules; token possession alone cannot select one. Stale decisions require a fresh preview
and renewed decision. Facts are never transferred or used to automatically satisfy new work. Apply
retries reuse requestId and identical inputs. Creation/configuration are not automatic retry operations;
after an uncertain result inspect current state before attempting another creation.

General loads these operations through task_management. Week Planning has the same management
capabilities. Day Work has occurrence completion/cancellation and explicit ending, plus recurrence
reads and successor preview; schedule application/creation remains a planning operation. Global
Planning reads directly and can use authorized delegated execution. Reflection and System Analysis
gain occurrence reads without recurrence writes; Reflection's existing capture/goal/note writes stay
limited. Delegated proposals expose reads/preview only. Delegated execution checks each target's task
list/context, forces range-query scope filters, requires an identified in-scope task for history, and
checks destination lists for creation. Tenant isolation remains enforced by host-bound persistence.
These scope checks retain the existing check-before-forward transaction boundary; concurrent source
assignment changes are not a frozen delegation snapshot.

`taskitem_complete` retains ordinary behavior and ends a recurring parent as a whole; chat must use
`task_occurrence_complete` for one performance. `taskitem_reopen` returns a safe error for recurrence,
pointing to explicit successor-based Resume. Parent focus edits never move an occurrence. Guidance
is shared by every mode and the delegated agent; reports remain dated snapshots and chat refreshes
targeted projection/history or relevant reports after actions. Scripted provider tests verify routing,
permissions and freshness; they are not a live-model evaluation. See [ADR-0042](adr/0042-shared-recurrence-mcp-chat.md).


## Series presentation and recovery (stage 8)

Planner Tasks stays one row per persisted task/series. Additive `recurrence` metadata on HTTP
summary/detail responses contains `summary`, `startDate`, nullable inclusive `endDate`, named
`missedPolicy`, soft `predecessorTaskId`/`successorTaskId`, `hasOutstandingWork` and
`previousSeriesHasOutstandingWork`. Ordinary rows have null recurrence metadata. Dates remain
YYYY-MM-DD strings and are displayed directly without browser timezone conversion.

The inspector shows rule interval/day selections, captured weekly alignment, bounds and missed
policy. Linked buttons open the previous/successor series independently of current list filters.
The outstanding indicator checks the full shortened interval of an open predecessor, respects all
terminal facts and effective full-period expiry, and excludes unavailable/closed sources. It is an
existence flag rather than an occurrence count. A successor's flag concerns its immediate previous
series; follow that series' links to inspect earlier ancestors. Missing/permanently deleted links
use existing not-found behavior. Reads share the Planner source snapshot and never reconcile.

Series ending is labelled separately from performed work. Complete/cancel one scheduled occurrence
in chat with its original date; ending a series records no extra performed work. Focus/deadline
metadata does not reschedule occurrences. Restoring a trashed series preserves history/links and
keeps it ended. Resume uses a chosen-start linked successor; recovery never fabricates work in the
inactive break. Permanent deletion removes only that series' facts/request ledger references,
without deleting other linked series.

Weekly Review shows exact pending occurrence totals separately from tasks/series source-page
counts; one row per series includes its pending occurrence count. Chat provides independent paged
occurrence/history drill-down. Actual completion activity and scheduled outcomes retain separate
selection bases. See [Planner workspace](planner-workspace.md), [review](weekly-review.md),
[ADR-0043](adr/0043-recurring-series-planner-readiness.md) and the
[verification evidence](recurring-tasks-verification.md).
