# ADR-0006: GDPR account deletion

**Date:** 2026-06-27  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

GDPR Art. 17 (right to erasure) requires that a user can permanently delete their account and all
associated personal data, self-service, with explicit confirmation, and that afterwards they can no
longer sign in and none of their data remains accessible in the running product.

OwnPlanner spreads a user's data across two SQLite stores (see `docs/database-schema.md` and
[ADR-0004](0004-gdpr-account-export.md)): the central `ownplanner-auth.db` (`AuthDbContext`) holds
the `User` row plus dependents — `PersonalAccessToken`, `PasswordResetToken`, `UserDailyUsage`,
`UserQuotaOverride`, all configured with `OnDelete(DeleteBehavior.Cascade)` — and a per-user
`ownplanner-user-{userId}.db` (`AppDbContext`) holds all planning entities. Chat conversation history
is in-memory only (`ChatSessionManager`), never persisted. OwnPlanner keeps no billing/accounting
records, so there is **no legal-retention exception**: erasure is total.

## Decision

A self-service, password-confirmed erasure endpoint.

### 1. Deletion service

`AccountDeletionService` (`OwnPlanner.Infrastructure/Account/AccountDeletionService.cs`, implementing
`IAccountDeletionService` in `OwnPlanner.Application/Account/`) takes the user id and their current
password:

1. Loads the `User` via `IUserRepository`; returns a failure result if not found.
2. Verifies the password with `IAuthService.VerifyPassword` (BCrypt). A mismatch returns
   `AccountDeletionResult(false, "Password is incorrect.")` and **deletes nothing**.
3. Deletes the `User` row via `IUserRepository.DeleteAsync`. The `AuthDbContext` foreign-key cascades
   remove all dependent auth rows in the same operation. This happens **first**, so login becomes
   impossible immediately.
4. Deletes the per-user planner database file via the new
   `IPlannerDbContextFactory.DeleteUserDatabaseAsync(userId, ct)`, implemented in
   `PlannerAppDbContextFactory` — it removes `ownplanner-user-{userId}.db` and its SQLite `-wal`/`-shm`
   side-car files. A leftover file after a failure is orphaned and unreachable (no surviving account
   resolves it).

### 2. Endpoint

`POST /api/account/delete` on `AccountController` (`[Authorize]`) with body
`DeleteAccountRequest { Password }`. It verifies + deletes via the service; on wrong password returns
`400 { message }`. On success it evicts the live chat session
(`IChatSessionManager.RemoveSessionAsync` keyed by the `SessionId` claim) so in-memory conversation
history is dropped, signs out the cookie (`HttpContext.SignOutAsync`), and returns
`200 { message: "Your account and all associated data have been permanently deleted." }`.

### 3. Frontend

A "Danger zone" section on the Settings page opens a confirmation dialog that states the action is
permanent and irreversible, lists what is removed, and requires the current password. On success the
client navigates to `/login` (the server has already signed the session out). `apiService.deleteAccount(password)`
posts the request.

## Consequences

### Positive

- Total, self-service erasure satisfying Art. 17 with a strong intent/identity check (password).
- A single `User` delete cascades all auth dependents — no per-table cleanup to keep in sync as the
  schema grows.
- Deleting the auth record first guarantees the account is unusable even if the file delete fails.

### Negative / Trade-offs

- The two stores are deleted sequentially without a distributed transaction; a crash between steps can
  orphan a planner DB file. Acceptable: the file is unreachable and can be reaped out-of-band. Revisit
  if regulatory audit requires provable byte-level erasure timing.
- Reuses `IAuthService` purely for `VerifyPassword`. Acceptable; a dedicated `IPasswordHasher`
  abstraction would be a larger refactor and is deferred until a second consumer needs it.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Account/IAccountDeletionService.cs` | Deletion service contract |
| `OwnPlanner.Application/Account/AccountDeletionResult.cs` | Result DTO |
| `OwnPlanner.Application/Account/DeleteAccountRequest.cs` | Request body (password) |
| `OwnPlanner.Infrastructure/Account/AccountDeletionService.cs` | Verify, cascade-delete auth, delete planner DB file |
| `OwnPlanner.Infrastructure/Persistence/IPlannerDbContextFactory.cs` | `DeleteUserDatabaseAsync` contract |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/PlannerAppDbContextFactory.cs` | Per-user DB file deletion (incl. WAL/SHM) |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/AccountController.cs` | `POST /api/account/delete`, session eviction + sign-out |
| `OwnPlanner.Web/ownplanner.web.client/src/services/api.ts` | `deleteAccount()` |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/SettingsPage.tsx` | "Danger zone" delete dialog |
