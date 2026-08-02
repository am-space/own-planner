# ADR-0008: Deterministic browser E2E testing

**Date:** 2026-08-02
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

OwnPlanner's most valuable user flows cross the React client, ASP.NET Core authentication, chat
orchestration, MCP tools, tenant-aware repositories, and per-user SQLite databases. Focused layer
tests do not prove that this complete path is wired correctly. Calling Gemini from ordinary browser
tests would make pull-request verification paid, nondeterministic, dependent on credentials, and
vulnerable to model/provider changes.

The originating implementation plan is archived at
[`docs/archive/e2e-testing-plan.md`](../archive/e2e-testing-plan.md).

## Decision

### 1. Run browser tests against a real ephemeral web server

`OwnPlanner.E2E.Tests` uses xUnit v3 and Playwright for .NET. A collection fixture starts the real web
application with `WebApplicationFactory<Program>` configured for Kestrel on an ephemeral loopback
port and serves the production Vite output. Chromium and the server run directly on the developer or
GitHub Actions runner, not inside the production Docker image.

The fixture creates a unique temporary authentication database and per-user database directory,
forces logging-only email and an empty Gemini key, validates that data paths stay under its temporary
root, and removes that root on disposal. E2E tests run without parallelization and use unique users.

### 2. Replace only model-adapter construction

The web server exposes an `IChatAdapterFactory` composition boundary. Production DI resolves
`GeminiChatAdapterFactory`, which preserves construction of `ChatServiceAdapter` from `ChatSettings`.
`ChatServiceFactory` still creates the session's real `DirectToolMcpAdapter` and passes it to the
model-adapter factory before constructing the real `PlanningService`.

The E2E fixture replaces that factory with `ScriptedChatAdapterFactory`. Its registry maps generated,
one-use prompts to explicit test scenarios. Scenarios may return text, fail, or call the session's
real MCP adapter. No test-only endpoint or production configuration switch can select the scripted
adapter.

### 3. Keep the first suite narrow and diagnostic

Initial tests cover protected routing, registration/logout, deterministic chat output, task
create/list through MCP and SQLite, tenant isolation, and provider failure behavior. Passing tests
discard Playwright traces. Failures retain a screenshot, trace, scoped server log, and TRX result in
`TestResults/E2E`; CI uploads that directory only when a job fails.

`scripts/setup.sh` installs Chromium. `scripts/verify.sh --e2e` builds the frontend and runs the E2E
project, while `--backend` filters out the `E2E` category and `--all` runs every verification path.

## Consequences

### Positive

- Pull-request E2E runs make no Gemini calls, need no Gemini secret, and have no per-run model cost.
- Tests cover the real browser, HTTP, authentication, planning, MCP, tenant-resolution, repository,
  and SQLite path while keeping responses deterministic.
- Unique temporary databases and a two-user scenario provide explicit tenant-isolation evidence.
- Failure-only browser artifacts keep successful runs light and make CI failures reproducible.

### Negative / Trade-offs

- Replacing the complete `IChatAdapter` leaves Gemini SDK payload mapping and its internal function-call
  loop outside browser coverage. Focused adapter tests own that boundary until a narrower provider
  session seam is justified.
- Chromium installation and the production frontend build add setup and verification time.
- The suite initially serializes E2E tests because the web chat session manager and test host are
  shared. Parallelization can be reconsidered after isolation is proven under concurrent execution.
- The production Docker image is built but not browser-tested as a container. Add a separate
  container smoke test if image-specific startup or packaging regressions become a recurring risk.

## Alternatives Considered

- **Call Gemini from browser tests** — rejected for ordinary verification because it costs money,
  needs secrets, and cannot provide stable assertions.
- **Mock HTTP responses in the React test layer** — rejected as the primary E2E strategy because it
  would skip authentication, server orchestration, MCP execution, tenant resolution, and SQLite.
- **Run the production Docker image for every E2E test** — deferred because in-process Kestrel gives
  equivalent application/network coverage for this slice with faster setup and easier dependency
  replacement. Container packaging remains a separate CI build.

## Deferred

Planning-mode coverage, quota behavior, account export/deletion, password reset, mobile viewports,
additional browsers, container smoke testing, and secret-gated live Gemini evaluation are follow-up
work. Live evaluation requires an explicit cost budget and nondeterministic quality criteria.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/IChatAdapterFactory.cs` | Replaceable model-adapter composition boundary |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/GeminiChatAdapterFactory.cs` | Production Gemini adapter construction |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ChatServiceFactory.cs` | Real MCP/planning composition using the factory |
| `OwnPlanner.E2E.Tests/Infrastructure/E2eWebApplicationFactory.cs` | Kestrel host, isolated databases, and test DI |
| `OwnPlanner.E2E.Tests/Infrastructure/ScriptedChatScenarioRegistry.cs` | One-use deterministic scenario selection |
| `OwnPlanner.E2E.Tests/ChatE2eTests.cs` | Chat, MCP persistence, and provider-failure coverage |
| `OwnPlanner.E2E.Tests/TenantIsolationE2eTests.cs` | Cross-user isolation coverage |
| `scripts/install-playwright.sh` | Cross-platform Chromium installation helper |
| `scripts/verify.sh` | Local and CI verification entry points |
| `.github/workflows/ci.yml` | E2E execution and failure-artifact upload |
