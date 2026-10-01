# Weekly reminders and goal-focused review

Configure Weekly review in Settings or ask General/Week Planning chat. Reminders start disabled.
Choose a timezone explicitly, a week-start day, and a local reminder time. The default suggestion is
Monday-start weeks with Sunday 18:00 delivery. Telegram is the supported proactive channel and
requires an enabled host integration plus a connected account. No timezone is inferred from the
server or browser. Disabling reminders suppresses automatic Telegram and chat offers; manual review
remains available after timezone selection.

Web and Telegram share preferences and review state even though their conversations are separate.
Settings can open a review, show goals, page through remaining tasks, finish it, skip it, or defer to a specified local time.
General and Week Planning chat can discuss priorities and apply explicitly requested changes. Other
chat modes retain their existing permissions. Telegram `/review` commands work in every mode without
switching it (see [Telegram commands](telegram-integration.md)).

## Calendar and selection

The review opens with every active goal. Goals needing attention appear first. Each goal shows the
exact number of linked tasks completed in the previous rolling seven days, a sample of up to ten
completed titles, the exact number of open tasks and up to ten open task titles. Goal metric and
current value appear when present. A goal has **no next step** when it has no open task, and is
**stalled** when it was created at least 14 days ago and no linked task was completed in the last
14 days. The application calculates these flags. The target period is **ending soon** when its
end date is within seven local calendar days, or **passed** after that date. Only strict
`yyyy-MM`, `yyyy-Qn`, `yyyy`, and fixed target dates are interpreted. Unrecognized periods have no
period flag.

Chat asks for a target-week commitment for every active goal. A user can choose an existing linked
task or create one, and schedule its focus date in the target week. A linked task can be planned
from within the review even if it was absent from the carryover page. A user may leave a goal
without a plan after one confirmation. Stalled or past-period goals prompt a keep, achieve, drop,
or target-period change decision. Metric progress can be updated during the review. Every goal
edit requires an explicit user instruction. When there are no active goals, the first opening of
that review offers once to formulate one to three goals; declining continues to tasks.

After the goal step, the ordinary task review continues. A task already linked to an active goal
and focused in the target week is omitted from the task page so it is not reviewed twice. The
assistant may suggest an active goal for an unlinked task when one clearly matches; linking
requires the user's confirmation. The summary reports exact completed work from the preceding
seven days and planned target-week work, each split between tasks linked to active goals and other
tasks. It lists active goals with no target-week plan. Trash and archived lists are excluded from
goal evidence and work-share counts. Links to achieved, dropped or missing goals count as other
work. Samples are bounded; counts remain exact.

Opening first resumes an unexpired due deferral, if one exists. Otherwise, on the last day of the configured local week, a new review targets next week. During the rest of
the week it targets the current week. Sunday's scheduled reminder and Monday's fallback therefore
refer to the same review. A review stores its target date, timezone and UTC boundaries permanently.
The remaining task page includes active incomplete tasks with at least one reason:

- Carryover: `FocusAt` before the target week's first calendar date. At the end of the week this
  includes current-week work, which is not labelled overdue just because it is unfinished.
- Overdue: `DueAt` strictly before the report instant, whether or not the task has a focus date.
- Target-week commitment: `DueAt` within the local target week, including its start and excluding
  its end, converted to UTC instants.

Focus dates use the existing stored calendar-date meaning. Their date fields are compared directly;
a UTC-labelled focus date is not shifted into a different local day. Deadline timestamps are instants.
DST gaps advance to the first valid local minute; repeated wall times use the earlier instant. Weeks
can therefore contain fewer or more than 168 hours.

Completed tasks, Trash and archived lists are excluded from the remaining task page. SQL produces exact counts and a deduplicated
page with every reason retained. Pages default to 20 and allow 1–50 tasks, ordered overdue first and
then by ID. Descriptions and note bodies are never queried. Notifications contain counts only.
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
| `POST {id}/transition` | Body: action complete/skip/defer and optional localTime yyyy-MM-ddTHH:mm |

Invalid input returns 400; another user's or missing review returns 404. The same Application service
backs `weekly_review_settings_get`, `weekly_review_configure`, `weekly_review_open`,
`weekly_review_transition`, `weekly_review_offer` and `weekly_review_apply` over direct, HTTP MCP and
stdio paths. Existing `weekly_report_get` retains its seven-day UTC contract.

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
