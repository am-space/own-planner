# ADR-0003: Outbound Email + Password Reset Flow

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

OwnPlanner has custom authentication (its own `AuthDb`, BCrypt password hashing, and personal
access tokens stored as SHA256 hashes) but, until now, **no outbound email and no
forgot/reset-password capability**. A user who forgot their password had no recovery path.

We need two things: a way to send transactional email, and a secure password-reset flow built on
top of it. Constraints shaping the design:

- **Clean-architecture layering** — the Application layer must not depend on a mail SDK. Email
  delivery is an outbound concern that belongs behind a port.
- **Reuse the existing token convention** — PATs already establish "store only a SHA256 hash of a
  prefixed random token; never persist plaintext." Reset tokens should mirror this exactly rather
  than invent a new scheme.
- **Anti-enumeration** — reset endpoints must not reveal whether an email is registered.
- **Backend-only for v1.5** — endpoints, token storage, sending, and templates ship now; the React
  reset page is deferred. The email links to a frontend route to be implemented later.

## Decision

### 1. Email as an outbound port with swappable adapters

Define `IEmailSender` (`OwnPlanner.Application/Email/IEmailSender.cs`) with a single
`SendAsync(to, subject, htmlBody, ct)`. Two Infrastructure adapters implement it:

- **`SmtpEmailSender`** (MailKit) — connects with `SecureSocketOptions.StartTls` on port 587;
  Brevo is the intended relay. `try/catch` with structured logging.
- **`LoggingEmailSender`** — logs the full message (including the reset link) and sends nothing.
  For local development only.

Adapter selection is **config-driven** via `Email:Provider` (`Smtp` | `Logging`). When the value is
blank, the provider defaults by environment: `Logging` in Development, `Smtp` otherwise. When `Smtp`
is selected, startup **fails fast** if `Email:Host` or `Email:FromAddress` is missing — this prevents
a misconfigured production deployment from silently falling back to logging reset tokens.

### 2. Configuration via bound instance (not `IOptions<T>`)

`EmailOptions` is bound with `GetSection("Email").Get<EmailOptions>()` and registered as a singleton
instance, following the `UsageQuotaOptions` convention rather than `Configure<T>`/`IOptions<T>`.
Adapters and `AuthService` take `EmailOptions` directly. Keys: `Provider`, `Host`, `Port`, `User`,
`Password`, `FromAddress`, `FromName`, `ResetUrlBase`, `ResetTokenLifetimeMinutes`.

### 3. Reset token mirrors the PAT pattern

`PasswordResetToken` (`OwnPlanner.Domain/Users/`) is a single-use, expiring entity in the auth
database. The plaintext token is `oprt_` + 32 random bytes (hex); only its SHA256 hash is persisted.
`IsActive(now)` requires `ConsumedAt is null && ExpiresAt > now`; `Consume()` stamps `ConsumedAt`.
Persistence is `PasswordResetTokenRepository` over `AuthDbContext`, added via the
`AddPasswordResetTokens` migration.

### 4. Issuance and redemption flow (`AuthService`)

- **Request** (`RequestPasswordResetAsync`): look up the user; if unknown/inactive, log and return
  silently (anti-enumeration). If `Email:ResetUrlBase` is unconfigured, log an error and return
  **without** issuing a token, since the link would be unusable. Otherwise invalidate the user's
  prior active tokens (only the most recent link stays valid), persist a new token, build the link
  `{ResetUrlBase}/reset-password?token=...`, and dispatch the email. The whole method is wrapped in
  try/catch so failures never surface to the caller.
- **Redeem** (`ResetPasswordAsync`): validate the token hash and expiry, resolve an active user, then
  **consume the token first** and persist it, **then** update the password. This ordering means a
  partial failure leaves the token spent and the password unchanged — the safe direction (the user
  simply requests a new link) rather than leaving a redeemable token after a password change.

### 5. API surface

`AuthController` exposes `POST /api/auth/forgot-password` and `POST /api/auth/reset-password`. Both
always return a generic 200 ("if an account exists…" / "password has been reset") regardless of
outcome, preserving anti-enumeration.

## Consequences

### Positive

- **Layering preserved** — MailKit lives only in Infrastructure; Application depends on `IEmailSender`.
- **Safe-by-default config** — production defaults to real SMTP and refuses to start misconfigured,
  so reset tokens are not logged in production.
- **Consistent token security** — reuses the proven PAT hashing/expiry approach; no plaintext at rest.
- **No unusable tokens** — issuance is skipped when no link can be built.
- **Failure errs safe** — consume-before-update ordering avoids redeemable-token-after-reset.
- **Covered by tests** — `PasswordResetServiceTests` exercises issuance, unknown/expired/consumed
  tokens, missing/inactive users, the missing-`ResetUrlBase` guard, and password validation.

### Negative / Trade-offs

- **Token issuance is not atomic across concurrent requests.** Invalidate-then-insert runs as
  separate saves, so two simultaneous requests for the same user could leave more than one active
  token. Accepted for a single-user-oriented personal planner: `AuthService` cannot open a
  transaction without a Unit-of-Work port, which would cut against the established "each repository
  method saves itself" convention. Revisit with a transaction-scope port or a partial unique index
  on `(UserId) WHERE ConsumedAt IS NULL` if real concurrency emerges.
- **Redemption spans two saves** (token, then password). Mitigated by ordering rather than removed.
- **Frontend reset page deferred** — the emailed link targets a route the SPA does not yet implement.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Email/IEmailSender.cs` | Outbound email port |
| `OwnPlanner.Application/Email/EmailOptions.cs` | Bound config model |
| `OwnPlanner.Application/Email/EmailTemplates.cs` | Password-reset HTML body builder |
| `OwnPlanner.Application/Auth/AuthService.cs` | Issuance + redemption flow |
| `OwnPlanner.Application/Auth/IAuthService.cs`, `AuthDtos.cs` | Service contract + request DTOs |
| `OwnPlanner.Domain/Users/PasswordResetToken.cs` | Single-use, expiring token entity |
| `OwnPlanner.Domain/Users/IPasswordResetTokenRepository.cs` | Repository port |
| `OwnPlanner.Infrastructure/Adapters/SmtpEmailSender.cs` | MailKit SMTP adapter (StartTls) |
| `OwnPlanner.Infrastructure/Adapters/LoggingEmailSender.cs` | Dev-only logging adapter |
| `OwnPlanner.Infrastructure/Repositories/PasswordResetTokenRepository.cs` | Token persistence |
| `OwnPlanner.Infrastructure/Persistence/AuthDbContext.cs` | `PasswordResetToken` model config |
| `OwnPlanner.Infrastructure/Migrations/AuthDb/20260618170108_AddPasswordResetTokens.cs` | Schema migration |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/AuthController.cs` | `forgot-password` / `reset-password` endpoints |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` | Provider selection + DI + startup fail-fast |
| `OwnPlanner.Web/OwnPlanner.Web.Server/appsettings.json` | `Email` config section |
| `OwnPlanner.Application.Tests/Auth/PasswordResetServiceTests.cs` | Reset-flow tests |
| `docs/email-configuration.md` | Operator configuration guide |
| `docs/archive/email-sending-plan.md` | Original implementation plan (archived) |
