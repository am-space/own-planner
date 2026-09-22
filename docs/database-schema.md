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
