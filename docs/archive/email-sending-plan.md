# OwnPlanner — Email Sending + Password Reset (v1.5)

> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0003: Outbound Email + Password Reset](../adr/0003-email-sending-password-reset.md).
> Details below may not reflect later refinements made during review.

## Context

OwnPlanner currently has custom auth (own `AuthDb`, BCrypt password hashing, PATs with SHA256-hashed
tokens) but **no email infrastructure and no forgot/reset-password endpoints**. Users who forget their
password have no recovery path. This feature adds a provider-agnostic email port + SMTP adapter (via
MailKit, Brevo as the relay) and a secure, anti-enumeration password-reset flow that mirrors the
existing PAT token pattern.

**Decisions (confirmed):**
- **Backend only** for v1.5 — endpoints, token storage, email sending, templates. React reset page deferred; the email links to a route that the frontend will implement later.
- **Adapter selection is config-driven** via `Email:Provider` (`Smtp` | `Logging`), defaulting to `Logging` in Development and `Smtp` otherwise.
- **Reset link** is built from a configured base URL: `{Email:ResetUrlBase}/reset-password?token=...`.

### Key codebase facts that shape this plan
- **No `AddInfrastructure` DI extension exists** — all DI is wired directly in `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs`. We will register email services there to match convention.
- **Token hashing convention** (`OwnPlanner.Application/Auth/AuthService.cs`): plaintext = `prefix_` + 32 random bytes hex; hash = `Convert.ToBase64String(SHA256.HashData(...))`. Reset tokens reuse this exactly with an `oprt_` prefix.
- **Options patterns**: `ChatSettings` via `Configure<T>(GetSection("Chat"))`; `UsageQuotaOptions` via direct singleton. We follow the `UsageQuotaOptions` approach for `EmailOptions` — bind with `GetSection("Email").Get<EmailOptions>()` and register the resulting instance as a singleton (adapters take `EmailOptions` directly, not `IOptions<EmailOptions>`).
- **AuthDb entities** live in `OwnPlanner.Domain/Users/`, configured in `OwnPlanner.Infrastructure/Persistence/AuthDbContext.cs`, migrations in `OwnPlanner.Infrastructure/Migrations/AuthDb/`. `User.Email` has a unique index; lookups via `IUserRepository.GetByEmailAsync`.

---

## Build order

### 1. Email port + options (Application layer)
- `OwnPlanner.Application/Email/IEmailSender.cs`
  ```csharp
  public interface IEmailSender
  {
      Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
  }
  ```
- `OwnPlanner.Application/Email/EmailOptions.cs` — bound from `"Email"` section:
  ```csharp
  public sealed class EmailOptions
  {
      public string Provider { get; set; } = "Logging"; // "Smtp" | "Logging"
      public string Host { get; set; } = "";
      public int Port { get; set; } = 587;
      public string User { get; set; } = "";
      public string Password { get; set; } = "";
      public string FromAddress { get; set; } = "";
      public string FromName { get; set; } = "OwnPlanner";
      public string ResetUrlBase { get; set; } = "";   // e.g. https://controlcode.space
      public int ResetTokenLifetimeMinutes { get; set; } = 30;
  }
  ```

### 2. Adapters (Infrastructure layer) + NuGet
- Add **`MailKit`** PackageReference to `OwnPlanner.Infrastructure/OwnPlanner.Infrastructure.csproj` (alongside existing EF Core / Mscc.GenerativeAI / Serilog refs).
- `OwnPlanner.Infrastructure/Adapters/SmtpEmailSender.cs` — implements `IEmailSender` using MailKit `SmtpClient` + `MimeMessage`; takes `EmailOptions` directly; `try/catch` + structured logging around send. Use `SecureSocketOptions.StartTls` for port 587.
- `OwnPlanner.Infrastructure/Adapters/LoggingEmailSender.cs` — implements `IEmailSender`; logs `to`, `subject`, and the full body (so the reset link is visible) at Information level. No network.

### 3. Reset-token storage (Domain + Infrastructure) — mirrors PAT
- Domain entity `OwnPlanner.Domain/Users/PasswordResetToken.cs` (inherit `EntityBase`):
  - `UserId` (Guid, FK → User, cascade delete)
  - `TokenHash` (string, unique index) — SHA256→Base64 of plaintext
  - `ExpiresAt` (DateTime)
  - `ConsumedAt` (DateTime?) — single-use flag; behavior method `Consume()`
  - helper `bool IsActive(DateTime now)` → not consumed && not expired
- Repository port `OwnPlanner.Domain/Users/IPasswordResetTokenRepository.cs`:
  - `AddAsync`, `UpdateAsync`, `FindActiveByTokenHashAsync(string hash)` (mirror `PersonalAccessTokenRepository.FindActiveByTokenHashAsync` — filter `ConsumedAt == null && ExpiresAt > now`), and optionally `InvalidateExistingForUserAsync(Guid userId)` to consume prior tokens on new request.
- Implementation `OwnPlanner.Infrastructure/Repositories/PasswordResetTokenRepository.cs` extending `RepositoryBase<PasswordResetToken, AuthDbContext>`.
- Register `DbSet<PasswordResetToken>` + entity config (unique index on `TokenHash`, index on `UserId`) in `OwnPlanner.Infrastructure/Persistence/AuthDbContext.cs`.
- **Migration** (CLI only — never hand-write):
  ```sh
  dotnet ef migrations add AddPasswordResetTokens --project OwnPlanner.Infrastructure --context AuthDbContext --startup-project OwnPlanner.Web/OwnPlanner.Web.Server
  ```

### 4. Reset flow (Application + Web)
- Extend `IAuthService` / `AuthService` (`OwnPlanner.Application/Auth/`) with:
  - `RequestPasswordResetAsync(string email, CancellationToken)` — look up user via `GetByEmailAsync`; if found, generate plaintext token (`oprt_` + hex), hash & store with expiry, build link `{ResetUrlBase}/reset-password?token={plaintext}`, send via `IEmailSender`. **Always completes without revealing existence** (anti-enumeration); swallow/log send errors.
  - `ResetPasswordAsync(string token, string newPassword, CancellationToken)` — hash token, `FindActiveByTokenHashAsync`; if valid, set `User.PasswordHash = HashPassword(newPassword)`, `Consume()` the token, persist. Return a generic success/failure result (no enumeration detail).
- Add DTOs to `OwnPlanner.Application/Auth/AuthDtos.cs`:
  ```csharp
  public record ForgotPasswordRequest(string Email);
  public record ResetPasswordRequest(string Token, string NewPassword);
  ```
- Add endpoints to `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/AuthController.cs`:
  - `POST /api/auth/forgot-password` → calls `RequestPasswordResetAsync`, **always returns 200** regardless of outcome.
  - `POST /api/auth/reset-password` → calls `ResetPasswordAsync`; 200 on success, 400 on invalid/expired token (generic message).
- Email template: a small static HTML helper (e.g. `OwnPlanner.Application/Email/EmailTemplates.cs`) producing the reset email body containing the link and expiry notice. Keep it minimal; reuse from `RequestPasswordResetAsync`.

### 5. DI wiring (Program.cs)
In `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs`:
- `var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();` then `builder.Services.AddSingleton(emailOptions);` (mirrors the `UsageQuotaOptions` registration; adapters receive `EmailOptions` directly).
- Register `IEmailSender` by provider:
  ```csharp
  var emailProvider = builder.Configuration["Email:Provider"]
      ?? (builder.Environment.IsDevelopment() ? "Logging" : "Smtp");
  if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
      builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
  else
      builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
  ```
- `builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();` (next to the existing repo registrations).

### 6. Config + secrets
- Add an `"Email"` section to `OwnPlanner.Web/OwnPlanner.Web.Server/appsettings.json` with empty/placeholder values and `"Provider": "Logging"`, `"ResetUrlBase": ""`.
- **Secrets (Host/User/Password/FromAddress) go in user-secrets or env vars — never committed.** Document the keys (`Email:Host`, `Email:User`, `Email:Password`, etc.) and Brevo SMTP setup in `docs/`.

### 7. Provider & deliverability (ops, outside code)
- Use **Brevo** transactional SMTP (free 300/day, EU region for GDPR). Provider switch later = config only.
- On `controlcode.space`: configure **SPF + DKIM + DMARC** (required for deliverability regardless of provider).

---

## Files to create / modify
**Create:**
- `OwnPlanner.Application/Email/IEmailSender.cs`
- `OwnPlanner.Application/Email/EmailOptions.cs`
- `OwnPlanner.Application/Email/EmailTemplates.cs`
- `OwnPlanner.Infrastructure/Adapters/SmtpEmailSender.cs`
- `OwnPlanner.Infrastructure/Adapters/LoggingEmailSender.cs`
- `OwnPlanner.Domain/Users/PasswordResetToken.cs`
- `OwnPlanner.Domain/Users/IPasswordResetTokenRepository.cs`
- `OwnPlanner.Infrastructure/Repositories/PasswordResetTokenRepository.cs`
- new EF migration under `OwnPlanner.Infrastructure/Migrations/AuthDb/`

**Modify:**
- `OwnPlanner.Infrastructure/OwnPlanner.Infrastructure.csproj` (MailKit ref)
- `OwnPlanner.Infrastructure/Persistence/AuthDbContext.cs` (DbSet + config)
- `OwnPlanner.Application/Auth/IAuthService.cs` + `AuthService.cs` (two methods)
- `OwnPlanner.Application/Auth/AuthDtos.cs` (two DTOs)
- `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/AuthController.cs` (two endpoints)
- `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` (Options + DI)
- `OwnPlanner.Web/OwnPlanner.Web.Server/appsettings.json` (`Email` section)

---

## Tests (xUnit v3 + FluentAssertions + NSubstitute, per layer)
- **Application.Tests** — `AuthService`:
  - `RequestPasswordResetAsync` sends email + stores hashed token when user exists; **no throw / no email** when user absent (and still returns normally).
  - `ResetPasswordAsync` updates hash + consumes token on valid token; rejects expired / consumed / unknown token; verifies stored token is hashed (not plaintext).
  - Mock `IEmailSender` and `IPasswordResetTokenRepository` via NSubstitute.
- **Infrastructure.Tests** (if present for repos) — `PasswordResetTokenRepository.FindActiveByTokenHashAsync` filters consumed/expired correctly.

---

## Verification (end-to-end, local)
1. `dotnet build OwnPlanner.sln -c Release` and `dotnet test` — all green.
2. Run web server: `dotnet run --project OwnPlanner.Web/OwnPlanner.Web.Server` (Development ⇒ `LoggingEmailSender`).
3. `POST /api/auth/forgot-password` with a **registered** email ⇒ 200; confirm the reset link (with `oprt_...` token) appears in the server logs.
4. `POST /api/auth/forgot-password` with an **unregistered** email ⇒ also 200, no log link (anti-enumeration confirmed).
5. Copy the token; `POST /api/auth/reset-password` with `{ token, newPassword }` ⇒ 200. Re-using the same token ⇒ 400. Expired token ⇒ 400.
6. `POST /api/auth/login` with the new password ⇒ succeeds.
7. (Pre-prod) Set `Email:Provider=Smtp` + Brevo creds in user-secrets, repeat steps 3 & 5, confirm real email delivery and SPF/DKIM/DMARC pass (e.g. mail-tester.com).
