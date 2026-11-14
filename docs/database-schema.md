# Conceptual Database Schema

OwnPlanner uses a decentralized SQLite data storage strategy separating authentication data from individual user application data. This ensures high isolation and easier backup/portability of user-specific planning data.

## Database Separation

The architecture maintains two distinct categories of databases:

1.  **Auth Database (`ownplanner-auth.db`)**
    *   A single, central database instance.
    *   Responsible for storing user accounts, credentials, and session information.
    *   Provides the mapping to determine which User Database to mount when a user logs in.
    *   Stores Telegram connection-token hashes, one-to-one Telegram account/chat mappings, the
        selected Telegram planning mode, and processed Telegram update IDs. All user-owned Telegram
        rows cascade-delete with the account; processed update IDs contain no message content.

2.  **User Databases (`ownplanner-user-{userId}.db`)**
    *   Dynamically created on a per-user basis.
    *   Each user has their own completely isolated SQLite file.
    *   Stores all application entities (Tasks, Notes, Goals, etc.).
    *   Accessed via a tenant-aware Entity Framework Core `DbContext`.

## Recoverable task deletion

`TaskItems` stores two task-list identifiers for different purposes:

- `TaskListId` is the required logical/original destination retained for display and restoration.
- `ActiveTaskListId` is the nullable foreign key used while a task is active. It cascades when an
  active task list is deleted and is cleared when the task enters Trash.

`TrashedAt` is a nullable UTC timestamp. Normal repositories, planner reads, and strategic, weekly,
and reflection reports explicitly select only rows where it is null. Trash queries select only rows
where it is set. Restoring verifies that `TaskListId` still resolves in the current user's database
before re-establishing `ActiveTaskListId`; a missing list is reported rather than replaced silently.
Only an already-trashed row can be permanently deleted.

## Recurring series and terminal occurrence facts

`AddRecurringTaskLifecycle` changes only AppDbContext. Existing ordinary task values are preserved;
their new `Recurrence`, `RecurrenceCheckedBefore` and `RecurrenceCalendarKey` columns are null,
and `RecurrenceRevision` defaults to zero. No AuthDbContext migration or data backfill is needed.

| Column/table | Stored contract |
| --- | --- |
| `TaskItems.Recurrence` | Nullable JSON schedule with validated frequency/interval/day selections/captured week start, ISO DateOnly start/inclusive end and explicit missed policy |
| `TaskItems.RecurrenceCheckedBefore` | Nullable ISO DateOnly exclusive processed-prefix boundary; no user-visible UpdatedAt change for checkpoint-only progress |
| `TaskItems.RecurrenceCalendarKey` | Resolved timezone/week-start configuration; a changed key rescans from the original start without deleting facts |
| `TaskItems.RecurrenceRevision` | Integer concurrency token advanced on recurring task writes, including bulk restore/goal-link operations |
| `TaskItems.RecurrencePredecessorTaskId` / `RecurrenceSuccessorTaskId` | Nullable soft GUID links with individual unique indexes; identify one linear continuation without cascading deletion across series |
| `TaskOccurrenceFacts` | Composite primary key `(TaskId, OccurrenceDate)`; DateOnly `YYYY-MM-DD`, Status Completed=0/Skipped=1/Cancelled=2, nullable actual UTC ISO `CompletedAt` |
| `RecurringSuccessorOperations` | Tenant-local GUID `RequestId` primary key, unique source/successor task IDs and a 64-character SHA-256 request fingerprint; no copied task text or facts |

A check constraint rejects undefined fact statuses and requires a completion timestamp exactly for
Completed. An index on CompletedAt supports later completion-activity queries. There are no pending
rows. Facts cascade from their TaskItem on permanent deletion or active-list cascade deletion;
Trash clears the existing active-list relationship and preserves facts. Restoring a recurring task
does not reopen it. Recurring administrative closure leaves parent CompletedAt null.
`AddRecurringSuccessors` adds only the nullable links and request ledger to AppDbContext, preserving
existing tasks, recurrence JSON and facts. Replacement changes only the old schedule's inclusive end
to the earlier of its existing end and the successor start minus one day. Retired empty ranges may
have end before start; ordinary/new schedule construction remains strict. The original rule/start/
policy and all facts retain their identities, including facts outside shortened bounds.
Successful request records commit with both linked tasks and cascade away when either task is
permanently removed; the soft links do not cascade into a neighboring series. Export's SQLite
snapshot includes these tables and columns automatically; account erasure removes the user database as before.
See [recurring tasks](recurring-tasks.md) for transaction, reconciliation and activation rules.

## Weekly review data

Per-user `WeeklyReviewPreferences` stores the explicit timezone, week start, local reminder time,
channel and disabled-by-default opt-in. `WeeklyReviews` stores target calendar dates and frozen UTC
boundaries, lifecycle state, deferral and separate delivery/offer claims. A unique target-week index
and serialized SQLite transitions coordinate workers. `AddWeeklyReviews` is an additive AppDbContext
migration; no auth schema changes are required. These rows follow planner export and account erasure.
See [weekly review](weekly-review.md) for selection and expiration rules.

Weekly review persistence uses Infrastructure row types mapped to Application workflow snapshots.
The singleton preference key and review identifiers retain their original columns and values.
`SeparateWeeklyReviewPersistenceModels` is a snapshot-only migration with empty Up/Down operations;
existing planner data needs no conversion.
`TrackWeeklyGoalCreationOffer` adds a per-review boolean to claim the one-time goal-creation
invitation in chat. Existing review rows default to unclaimed; no auth data changes.

## Goal pause and resume

`Goals.Status` retains Active=0, Achieved=1 and Dropped=2; Paused is appended as 3. Nullable UTC
`PausedAt` records the current pause; `LastResumedAt` records the latest resume for the 14-day stalled
grace period. Resuming clears `PausedAt` without changing task links.

`AddPausedGoals` adds those nullable columns, nullable `WeeklyReviewPreferences.PausedGoalsMentionMonth`
(the first day of the claimed local month), and `WeeklyReviews.ActiveGoalLimitWarned` (default false; becomes true after the initial count check even
when no warning was needed).
All claims are per-user and serialized in the existing SQLite transaction. Existing statuses and
planning data are preserved. Central auth is unchanged; exports retain these planner fields.


## Recurrence portability and final integration

Planner recurrence summaries, bounds and predecessor outstanding-work flags are read projections;
they add no persisted columns or pending occurrence rows. Final presentation stage 8 adds no
migration. Existing databases upgrade through `AddRecurringTaskLifecycle` and
`AddRecurringSuccessors` during normal per-user initialization. Migration regressions preserve
ordinary task fields and existing recurrence/fact data.

Account exports remain tenant-bound `VACUUM INTO` snapshots. The exported SQLite database includes
recurrence JSON, original fact dates/actual timestamps, soft series links, reconciliation metadata
and successor retry ledger, including preserved facts outside shortened generation bounds. It
excludes the central authentication database. Exports can be opened independently; OwnPlanner has
no new browser import/recovery flow. Account deletion removes the authenticated user's database
and sidecars through the existing factory; it does not touch another user's series or history.
Trash restoration is separate: it keeps a recurring series ended and preserves facts/links.
See [acceptance evidence](recurring-tasks-verification.md) for real export/deletion, migration and
recovery tests and [ADR-0043](adr/0043-recurring-series-planner-readiness.md) for presentation decisions.
