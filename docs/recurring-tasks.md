# Recurring-task foundations and staged contracts

Stages 1 and 2 ([#89](https://github.com/am-space/own-planner/issues/89),
[#90](https://github.com/am-space/own-planner/issues/90)) provide internal recurrence types, a
deterministic calendar engine, per-user persistence and shared lifecycle reconciliation. Public
recurring-task creation, successor commands, history queries, MCP operations, report integration
and browser features remain unavailable. The [active implementation plan](recurring-tasks-plan.md)
tracks all eight stages of [#84](https://github.com/am-space/own-planner/issues/84).
The later-stage sections below describe required future behavior.

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
| `ITaskOccurrenceHistory` | Future separate read queries by scheduled date or actual completion instant; no implementation yet |

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
from an occurrence date and never multiplied into one deadline per occurrence. Later unified
reads must distinguish series-level attention from occurrence work items and deduplicate their
counts. Neither field will be copied automatically to a successor. Ordinary task fields, completion,
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
reopen directs callers to explicit successor-based resume (stage 3); ordinary reopen is unchanged.
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

## Successor and resumption contracts for stage 3

Given requested start D, atomically shorten the old generation end to the earlier of its existing
end and D minus one day; never extend it. Preserve its original rule. Create one linked successor
starting on D, copying title, description, list, goal, importance and missed policy unless that
policy is explicitly changed. Do not copy `DueAt` or manual `FocusAt`. A cutoff before the old
start may leave an empty retired interval; stage 3 must represent that explicitly without weakening
new-series validation. The old occurrence dates and facts always retain their old task ID.

For example, replacing a Friday schedule with Mondays starting November 16 leaves unfinished
November 13 work on the predecessor, independently of the successor's November 16 occurrence.
The old occurrence expires at its full original week end, never at the replacement cutoff.

Preview must show all affected future facts and the proposed successor schedule. For an advance
completion on November 20 and requested start November 16, require an explicit choice: show a
later start beyond the affected completion dates, or keep November 16 and authorize successor
occurrences as separate work. Keep original identity, status and CompletedAt even if the fact
falls outside the old shortened bounds. Cancellation facts are preserved too; nothing transfers.

Apply must revalidate the previewed parent revision, schedule, selected resolution and affected fact
set in the same transaction as the cutoff/link/creation. A changed snapshot requires a refreshed
preview, not a stale mutation. A tenant-scoped request key must return the original successful
result on retry and reject reuse with different inputs. Concurrent changes must not create sibling
successors or partially shorten a parent. These preview/apply storage/service contracts are to be
implemented in stage 3.

Resumption uses the same successor mechanism for explicitly or automatically completed series.
For an old series ended November 15 and a successor beginning December 7, the intervening dates
produce no obligations or skipped facts. Keep the old parent completed, use the selected new start
as the interval anchor, and do not inherit an invalid past end. Recurring `taskitem_reopen` must
direct callers to explicit resume; ordinary reopen remains compatible. Future-fact conflicts and
retry handling follow the replacement contract.

## Read contracts for stages 4–6

`ITaskOccurrenceHistory` separates facts selected by scheduled calendar date from facts selected
by completion UTC instant. Both have deterministic ordering, exact totals and bounded pages over
one snapshot; closed/predecessor facts remain queryable subject to existing data-access restrictions.
Facts outside generation bounds are history, not pending obligations to regenerate.

The unified planning projection must combine ordinary tasks with computed occurrences before
filtering, sorting, exact counting and paging. Rows address occurrences by the original composite
identity and expose date, full period, effective status and series metadata; ordinary rows retain
their existing task identity. Exact task/series counts and occurrence/work-item counts are separate.
Existing `taskitem_list_*` tools retain their stored-task meaning.

Retained past obligations require an explicit `includeRetainedBeforeStart` selection alongside the
bounded requested window. When selected, enumerate every eligible unresolved date from each series'
start through the window start, subtract facts and merge with the in-window rows, deduplicating by
identity. Sort by work date then task ID (ordinary rows use an explicit kind tie-breaker) and apply
the page only after merging. Return `totalCount`, `offset`, `limit` and `hasMore`; any operational
limit must be disclosed and cannot pretend to be an exact total. Never silently apply a lookback
cap that makes older obligations unreachable. The persistence/query implementation belongs to
stage 4 and may optimize enumeration/counting without materializing pending facts.

Active planning excludes completed parents, Trash and archived lists; history can still include
authorized closed/predecessor series. Reports, preloads and Weekly Review must consume shared
projection/lifecycle behavior before public series creation is enabled. No stage may count parent
closure as extra work or infer duration/capacity from repeated occurrence counts.
