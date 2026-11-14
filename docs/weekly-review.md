# Weekly reminders and goal-focused review

Open **Weekly Review** next to Tasks in the desktop navigation or mobile navigation drawer. The
dedicated authenticated page at `/planner/review` opens the shared review and supports direct links
and browser refresh. It shows the target week, timezone, status, existing goal guidance, carryover,
overdue deadlines and upcoming commitments, with task pagination and Finish review, Skip this week
and Defer review controls. Its content scrolls within the planner workspace, alongside the existing
assistant (or the switchable chat surface on mobile). Opening and paging never change tasks.

Weekly Review preferences are the first section in Settings, before Telegram and access tokens.
The review page's **Review settings** link opens that section; **Open weekly review** in Settings
navigates to the dedicated page instead of rendering the workflow inline. Without a configured
timezone, the review page shows a setup prompt linking to preferences. Loading, request failures
with retry, and an empty remaining task list have explicit feedback.

Configure Weekly review in Settings or ask General/Week Planning chat. Reminders start disabled.
Choose a timezone explicitly, a week-start day, and a local reminder time. The default suggestion is
Monday-start weeks with Sunday 18:00 delivery. Telegram is the supported proactive channel and
requires an enabled host integration plus a connected account. No timezone is inferred from the
server or browser. Disabling reminders suppresses automatic Telegram and chat offers; manual review
remains available after timezone selection, including without a connected Telegram account. This
page reviews unfinished work and commitments for its target week; it does not add a completed-work
retrospective or task-editing controls.

Web and Telegram share preferences and review state even though their conversations are separate.
The Weekly Review page can show goals, page through remaining tasks, finish it, skip it, or defer to a specified local time.
General and Week Planning chat can discuss priorities and apply explicitly requested changes. Other
chat modes retain their existing permissions. Telegram `/review` commands work in every mode without
switching it (see [Telegram commands](telegram-integration.md)).

## Calendar preferences outside review

General, Week Planning, Day Work and Reflection also read the explicit timezone and week start,
even with reminders disabled. Ordinary calendar reports are read-only and do not open a review,
claim guidance or change reminder settings. Missing timezone uses a disclosed UTC fallback for
ordinary reports; manual weekly review retains its explicit-timezone requirement. Ordinary this-week
reports remain in the current week on the last day, independently of review targeting below.
See [user-calendar rules and MCP options](user-calendar.md).
General's [today-first attention overview](general-attention.md) uses the same current-week calendar
without opening a review; its sections and pages do not use the review's frozen target week.

## Paused goals and focus guidance

Ask chat to pause an active goal or resume a paused goal. The shared `goal_update` tool accepts
`status=Paused` and `status=Active`; pause/resume works in web and Telegram wherever goal editing is
permitted. Achieved and dropped goals cannot be paused. Repeated requests do not reset timestamps.
Pausing records `PausedAt`; leaving Paused clears it, and resuming records `LastResumedAt` for a new
14-day stalled grace period. Linked tasks stay linked and remain independently available.

Paused goals are excluded from the active goal step, every goal flag and reminder goal count. Ask
"show my paused goals" to retrieve them through `goal_list includeInactive=true`; the existing
Goals screen also has a Paused status filter. Tasks linked to paused goals may still appear in the
remaining task review and count as other work.

Once per local calendar month, the first actual live review opening with paused goals includes one
sentence naming them, their approximate pause ages in days/weeks/months, and an invitation to resume.
The Weekly Review page, web chat and Telegram `/review` share this claim. The month is determined by the current
opening instant in the review's frozen timezone, rather than its target-week date. Empty paused-goal
sets, task pagination, expired/finished reviews, reminders and lookups solely to skip/defer/finish do
not consume the mention. A review already presented has delivered its mention even if later skipped
or deferred. Claims survive restarts and are serialized across concurrent openings. Long Telegram
reviews retain all paused-goal names and ages: the bot client splits the full reply into messages of at most 4096
UTF-16 units, preserving Unicode content.

Five active goals is a recommendation. Creating or activating a goal above that count succeeds and
returns `activeGoalWarning`, which chat adds once to its response. Unrelated edits and repeated Active
updates do not repeat it. The first presented page checks the active-goal count once, shared across
channels, and returns the same warning only when that initial count exceeds five. Later refreshes
do not add a review warning if goal creation or resuming raises the count. No action is blocked by this
count. There is no automatic pausing, scheduled resume, configurable limit or new goal editing screen.

## Calendar and selection

The review opens with every active goal. Goals needing attention appear first. Each goal shows the
exact number of ordinary/occurrence work items completed in the previous rolling seven days,
a sample of up to ten completed titles deduplicated by source, the exact number of open task/series
records and up to ten open source titles. Goal metric and
current value appear when present. A goal has **no next step** when it has no open task/series record, and is
**stalled** when it was created or most recently resumed at least 14 days ago and no linked ordinary task or occurrence was completed in the last
14 elapsed days. The application calculates these flags. The target period is **ending soon** when its
end date is within seven local calendar days, or **passed** after that date. Only strict
`yyyy-MM`, `yyyy-Qn`, `yyyy`, and fixed target dates are interpreted. Unrecognized periods have no
period flag.

Chat asks for a target-week commitment for every active goal. A user can choose an existing linked
task or create one, and schedule its focus date in the target week. A linked task can be planned
from within the review even if it was absent from the carryover page. A user may leave a goal
without a plan after one confirmation. Pending original occurrence dates in the target week already
constitute a recurring plan; do not focus/reschedule them with ordinary task actions. Stalled or past-period goals prompt a keep, pause, achieve, drop,
or target-period change decision. Metric progress can be updated during the review. Every goal
edit requires an explicit user instruction. When there are no active goals, the first conversational
opening of that review offers once to formulate one to three goals, even if the Weekly Review page or Telegram
`/review` was opened earlier; declining continues to tasks.

After the goal step, the ordinary task review continues. A task already linked to an active goal
and focused in the target week is omitted from the task page so it is not reviewed twice. The
assistant may suggest an active goal for an unlinked task when one clearly matches; linking
requires the user's confirmation. The summary reports exact completed work from the preceding
seven days and planned target-week work, each split between work linked to active goals and other
work. It lists active goals with no target-week plan. Trash and archived lists are excluded from
goal evidence and work-share counts. Links to paused, achieved, dropped or missing goals count as other
work. Samples are bounded; counts remain exact.

Opening first resumes an unexpired due deferral, if one exists. Otherwise, on the last day of the configured local week, a new review targets next week. During the rest of
the week it targets the current week. Sunday's scheduled reminder and Monday's fallback therefore
refer to the same review. A review stores its target date, timezone and UTC boundaries permanently.
For ordinary tasks, the remaining task page includes active incomplete records with at least one reason:

- Carryover: `FocusAt` before the target week's first calendar date. At the end of the week this
  includes current-week work, which is not labelled overdue just because it is unfinished.
- Overdue: `DueAt` strictly before the report instant, whether or not the task has a focus date.
- Target-week commitment: `DueAt` within the local target week, including its start and excluding
  its end, converted to UTC instants.

Focus dates use the existing stored calendar-date meaning. Their date fields are compared directly;
a UTC-labelled focus date is not shifted into a different local day. Deadline timestamps are instants.
DST gaps advance to the first valid local minute; repeated wall times use the earlier instant. Weeks
can therefore contain fewer or more than 168 hours.

Completed parents/tasks, Trash and archived lists are excluded from the remaining task page. The
shared read snapshot produces exact source counts and a deduplicated page with every reason retained. Pages default to 20 and allow 1–50 tasks, ordered overdue first and
then by ID. Each recurring series remains one source row with pending/planned occurrence counts.
A pending target-week goal plan omits the series only when no earlier actionable occurrence remains.
An independent exact occurrence page includes full current periods and the entire retained prefix,
so Sunday next-week review never closes the current week. Descriptions and note bodies are never queried. Notifications contain counts only.
Opening and paging always retrieve fresh data. Paging is current-state paging; concurrent edits can
change membership, so refresh before applying a batch.

## Review and task actions

State is `notStarted`, `inProgress`, `completed`, `deferred` or `skipped`. Opening changes only
`notStarted` to `inProgress`. Delivery and individual task edits never imply review completion.
Completion and skipping are explicit and suppress further automatic offers for that period.
Deferral keeps the original review identity, resolves a definite local time in its stored timezone,
and must be in the future before its target week ends. Expired deferred reviews are not replayed. Invitations include the target date;
`weekly_review_open` can select that exact existing period with `targetWeek=yyyy-MM-dd` when multiple
periods are available, or resume by review ID.

A calendar preference edit reuses any stored target calendar week that overlaps the newly computed
one (start dates less than seven days apart), including completed/skipped periods. Its timezone,
boundaries and scheduled time remain frozen. This deliberately suppresses an overlapping period
instead of creating a second review; the next non-overlapping target uses the new settings. Reminder
time edits also apply to newly created reviews. Disabling takes effect immediately for future claims;
an already in-flight network request cannot be recalled.

The `weekly_review_apply` tool checks the current task, list and revision from a fresh review page
before each authorized operation. Missing, completed, trashed, archived, changed, expired or
out-of-review targets return `applied=false`. Supported actions are target-week focus scheduling,
clearing focus, completion, recoverable Trash, setting a deadline, and explicitly clearing a deadline.
Deadline clearing reuses #61's `clearDueAt` service behavior. Focus changes preserve deadlines, and
deadline changes preserve focus. Existing task tools remain available for other task management.
There is no batch rollback: report each confirmed result and any partial failures. Task actions use
the existing task service transaction boundaries; a separate concurrent edit after the precheck is
still possible. Opening/accepting a review does not authorize any task operation.
The additive `goalFocus` action accepts a linked task from the goal step or a targeted task lookup,
checks that its goal is still active, and places its focus date in the target week. The additive
`linkGoal` action accepts an unlinked task from the remaining task review and an active goal after
confirmation. Both actions apply the same review, list and revision guards. Existing task and goal
tools handle creation, metric updates, status and target-period changes.

## Delivery and fallback

The host runs a one-minute scheduler tick, processing up to four users concurrently with isolated
scopes and cancellation. One slow send therefore leaves capacity for other users. It enumerates
active, linked accounts from central auth
records and uses the same initialized per-user planner context as chat. Before sending it rechecks
that the original Telegram link still belongs to that active user and that reminders remain enabled.
No route, body, tool argument or model output selects the background user's database or destination.

SQLite write transactions serialize state transitions across workers. A nonempty reminder occurrence
is claimed and persisted before network I/O. Ordinary scheduled sends are eligible only between the
configured reminder time and target-week start. Missed previous weeks are never replayed. An
active goal alone makes a reminder eligible, even without carryover or overdue tasks. Empty reviews
with no active goals produce no send. Reminders and chat invitations include goal counts and flag
counts but no titles. Missing links and disabled Telegram integration leave chat fallback intact.
An explicit deferral creates another occurrence on the original review.

A send records `delivered` separately from review status. A Telegram HTTP 429 rejection permits at
most three total attempts with 15-minute spacing within the eligibility window. Other failures,
including ambiguous timeouts or server errors, are not automatically retried. A process crash after
claiming leaves a claim that is never resent. Exactly-once remote delivery cannot be guaranteed
across a crash or lost acknowledgement; this policy prevents duplicate retry bursts at the cost of
possibly missed messages. Exceptions are logged by type only, without tokens, destinations or text.

General and Week Planning can call `weekly_review_offer` after answering a suitable planning request.
Urgent and unrelated work should not trigger it. The Application service atomically claims at most
one counts-only invitation across channels when enabled, nonempty and undelivered, in the target week
or when a deferral becomes due. A pending send claim suppresses fallback for five minutes; a known
failure can fall back immediately when otherwise eligible. Delivered, completed, skipped, future
and previously offered reviews are suppressed. A review remains available explicitly on request.
The offer is claimed when the tool runs; a failed model response after that point may lose the
invitation, so automatic offers are best-effort. No mode changes or task mutations accompany offers.

## Contracts and persistence

Additive cookie-authenticated endpoints under `/api/weekly-review`:

| Endpoint | Behavior |
| --- | --- |
| `GET settings` | Read-only preferences lookup; unconfigured defaults create no rows |
| `PUT settings` | Explicit enabled, timezone, week start (0=Sunday…6=Saturday), HH:mm, telegram channel |
| `POST open?reviewId=&targetWeek=&offset=&limit=` | Open/resume and retrieve a fresh page |
| `GET {id}/occurrences?taskId=&offset=&limit=&occurrenceDate=` | Read independent occurrence details without opening/claiming guidance |
| `POST {id}/transition` | Body: action complete/skip/defer and optional localTime yyyy-MM-ddTHH:mm |

Invalid input returns 400; another user's or missing review returns 404. The same Application service
backs `weekly_review_settings_get`, `weekly_review_configure`, `weekly_review_open`,
`weekly_review_transition`, `weekly_review_offer`, `weekly_review_occurrences` and `weekly_review_apply` over direct, HTTP MCP and
stdio paths. Existing `weekly_report_get` retains its seven-day UTC contract when calendarPeriod is omitted; the
additive named-period path is documented in [user calendar](user-calendar.md).

Migration `AddWeeklyReviews` creates per-user `WeeklyReviewPreferences` and `WeeklyReviews`. It does
not alter tasks or central auth records. Initialization applies it lazily to user databases, including
users reached through scheduling. Review state and preferences are included in the existing planner
SQLite export and removed with the account's planner database. Standalone stdio exposes the same
preferences and workflow, but has no hosted Telegram scheduler.

The persistence row types belong to Infrastructure; Application owns detached workflow snapshots and
state transitions. `SeparateWeeklyReviewPersistenceModels` updates the EF snapshot for that separation
without changing tables, columns or existing data. Settings retain the same JSON fields and the MCP
getter publishes read-only and idempotent hints. Review-opening and offer tools still update workflow
state and are not labelled read-only.

`AddPausedGoals` adds nullable goal pause/resume timestamps, a nullable month claim in preferences,
and a false-by-default review warning claim. Existing goal status values are preserved: Active=0,
Achieved=1, Dropped=2, and the appended Paused=3. Existing databases need no data conversion; normal
per-user initialization applies the migration. Planner SQLite exports include these fields.

Review views add nullable `pausedGoalsMention` and `activeGoalWarning` presentation fields. The shared
`weekly_review_open` adds optional `present` (default true); use false for internal transition lookups
that do not present the review. These guidance fields never enter reminders. Like other claimed chat
offers, a response failure after a claim can lose the guidance; the claim prevents repeated prompts.


## Recurring work counts and guarded actions

`Goals.Work` and per-goal `Work` separate pending work, target-week scheduled outcomes and actual
completion activity. `WorkShare` and per-goal `PlannedWork` distinguish ordinary tasks, distinct
series and occurrences; `WorkItemCount` is ordinary tasks plus occurrences. Browser and Telegram
summaries use these mixed counts. Legacy completed counts/samples remain ordinary-task units;
legacy open/planned counts describe stored sources. Closure of a recurring parent contributes no
completed work. Progress/stalled calculations use actual `CompletedAt`, including historical
skipped-to-completed corrections and preserved predecessor facts, with the existing rolling seven
or fourteen elapsed days through the current report instant. Target-week scheduled outcomes use
original calendar dates in the frozen review timezone; never sum scheduled results and activity.

`Report.Occurrences` starts an independent pending page at offset 0, limit 20. Its `TotalCount`,
`SeriesCount`, `CarryoverCount` and `PlannedCount` are exact before paging; each item contains `Work`
with original `(TaskId, OccurrenceDate)`, status, full period and actual completion time, plus
`Revision` and `GuardToken`. Source metadata/deadlines appear once per returned series. Use the
read-only `weekly_review_occurrences` or HTTP endpoint for independent offset/limit (1–50), optional
series filtering, or `taskId` plus `occurrenceDate` to inspect a recorded historical fact. A historical
fact remains addressable after closure or schedule shortening. Reads never fabricate facts or
claim one-time goal/paused guidance. Current pages can change between requests.

The additive `weekly_review_apply` actions `completeOccurrence` / `cancelOccurrence` require the
original date and fresh guard alongside the existing reviewId/taskId/revision. They check review,
list, parent revision/schedule and current fact together under one SQLite write lock, then reuse
shared lifecycle policy. Stale changes, expiry or completion return `applied=false`; refresh and
resolve the new state before retrying. A successful completion preserves original identity and
sets actual completion time without completing the series. Omitted `completedAt` uses the injected
clock; an explicitly reported earlier actual instant may be supplied with Z/offset, no later than
now. Resolve ambiguous times first and never infer them from the scheduled date. Other actions
reject `completedAt`. A recorded skipped fact can be explicitly
corrected even on a closed predecessor. Duplicate stale attempts never move its completion time.
Ordinary review actions reject recurring parents; focus never reschedules an occurrence. Series
termination/replacement remains a separate workflow, and review guards never approve successor
conflict choices. No browser task editing or occurrence-expanded list is added.
