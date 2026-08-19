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
