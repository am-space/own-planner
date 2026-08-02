# End-to-end testing plan

> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0008: Deterministic browser E2E testing](../adr/0008-deterministic-browser-e2e-testing.md).
> Details below may not reflect later refinements made during review.

**Status:** Active
**Date:** 2026-08-02

## Intended outcome

Add a deterministic browser-based end-to-end test suite that exercises the real React application,
ASP.NET Core host, authentication, planning orchestration, MCP tools, tenant resolution, and isolated
SQLite databases without making paid or nondeterministic Gemini API calls during normal development
or pull-request verification.

## Temporary feature contract

This plan records the agreed scope for the direct feature request. There is no canonical GitHub issue
for this work, and this plan does not create or modify GitHub state.

### Acceptance criteria

| Criterion | Planned change | Test evidence |
| --- | --- | --- |
| Normal E2E runs never call Gemini | Add a replaceable chat-adapter creation seam and install a scripted adapter from the E2E test host | The E2E host resolves the scripted factory with no Gemini API key; a chat scenario passes in an environment with no Gemini credentials |
| Tests exercise the application through a real browser and network server | Add a Playwright for .NET xUnit v3 project and host the web application on Kestrel through `WebApplicationFactory<Program>` | Browser tests navigate, register, send chat messages, and assert rendered results |
| Chat tool scenarios use production application and persistence paths | Keep `ChatServiceFactory`, `PlanningService`, `DirectToolMcpAdapter`, MCP handlers, repositories, and SQLite real; replace only `IChatAdapter` creation | A scripted chat turn creates a task via `taskitem_create`, then a later turn lists it through the real tool path |
| E2E data cannot affect developer or production data | Configure a unique temporary auth database and user-database directory for every test host | Fixture assertions prove all configured database paths are under its temporary root; the root is cleaned on disposal |
| Tenant isolation is covered | Use two independent browser contexts and registered users | A task created by user A is absent from user B's list |
| Core authentication behavior is covered | Exercise protected routing, registration, logout, and subsequent unauthorized access | Browser assertions cover redirect, authenticated chat access, and logout |
| The suite integrates with repository verification | Add an explicit E2E verification command and CI setup for Chromium | The E2E command passes locally and in CI; `--all` includes it while backend-only verification remains focused |
| Failures are diagnosable without leaking user data or secrets | Retain Playwright trace/screenshot and scoped server logs on failure only | Playwright and CI configuration review proves failure-only retention under the temporary test root; a controlled provider-failure scenario verifies user-visible error handling |

## Design

### Test runner and host

Create `OwnPlanner.E2E.Tests`, targeting .NET 10 and using the repository's xUnit v3 stack plus
Playwright for .NET and `Microsoft.AspNetCore.Mvc.Testing`.

The fixture will:

1. Create a unique temporary root.
2. Configure `Database:AuthDbPath` and `Database:UserDbDirectory` below that root.
3. Configure safe E2E-only email, cookie, quota, and logging behavior.
4. Replace the chat-adapter factory with a scripted implementation from the test assembly.
5. Start the real application with Kestrel on an ephemeral port.
6. Create a fresh Playwright browser context for each test.
7. Dispose chat sessions, the host, browser state, and the temporary root after the run.

The test host must fail fast if its database paths escape the temporary root or if the production
Gemini adapter factory is resolved.

### Replaceable Gemini boundary

Introduce a small `IChatAdapterFactory` composition interface near the existing web chat services.
The production implementation will preserve the current behavior by constructing
`ChatServiceAdapter` from `ChatSettings`. `ChatServiceFactory` will continue to create and initialize
the real `DirectToolMcpAdapter`, then request an `IChatAdapter` from the injected factory and wrap both
in the real `PlanningService`.

The scripted E2E implementation will live only in `OwnPlanner.E2E.Tests`. It will support explicit,
test-owned scenarios for:

- a deterministic text response;
- a real MCP tool invocation followed by a deterministic response derived from the tool result;
- a controlled provider failure;
- stable token/context metadata where a UI scenario requires it.

Scenario selection must be isolated per test/user and must not rely on one process-global FIFO that
would make parallel execution order-dependent.

This first slice deliberately replaces the complete `IChatAdapter`. Consequently, Gemini SDK payload
mapping and the function-call loop internal to `ChatServiceAdapter` remain outside browser E2E
coverage. Existing and new focused adapter tests will own that boundary; a lower-level Gemini-session
abstraction is deferred unless adapter behavior proves too difficult to test safely.

### Initial browser scenarios

1. An unauthenticated visitor to `/chat` is redirected to sign-in.
2. A new user registers and reaches the chat page with an authenticated cookie.
3. A scripted chat message creates a task through the real MCP and SQLite path.
4. A later scripted message lists the created task, including after clearing the chat session.
5. Two users cannot observe each other's task data.
6. Logout removes access to the protected chat route.
7. A scripted provider failure produces the existing user-visible error behavior.

Planning-mode, quota, account export/deletion, password-reset, mobile viewport, and broader browser
coverage are follow-up scenarios after this foundation is stable. Exhaustive MCP behavior stays in
the MCP and service test projects rather than being duplicated through the browser.

## Implementation order

1. Add `IChatAdapterFactory` and the production Gemini implementation, refactor
   `ChatServiceFactory` to use it, and add focused web-server tests proving construction and disposal
   behavior. The closest patterns are `IChatServiceFactory`/`ChatServiceFactory` and the existing
   `ChatSessionManagerTests`.
2. Add `OwnPlanner.E2E.Tests` with a Kestrel-backed application fixture, temporary database
   configuration, safe authentication options, and the scripted adapter/scenario registry.
3. Add reusable browser helpers for registration, chat interaction, and semantic message selection.
   Prefer accessible roles and labels; add minimal `aria-label` or stable test hooks to the React UI
   only where semantic selection is currently ambiguous.
4. Implement the initial scenarios, starting with authentication, then single-user tool persistence,
   then two-user isolation and error behavior.
5. Integrate Chromium installation and E2E execution into `scripts/setup.sh`, `scripts/verify.sh`, and
   CI. Keep an explicit `--e2e` path; make `--all` the final complete gate.
6. Update reference documentation and record the shipped architectural decision in an ADR. Archive
   this plan according to the documentation process when implementation is complete.

## Impact map

| Area | Impact |
| --- | --- |
| Domain | Not affected; no entity or invariant changes |
| Application | Existing `IChatAdapter`, `PlanningService`, and MCP contracts remain unchanged |
| Infrastructure | Production Gemini adapter behavior remains unchanged; only construction may move behind a small factory |
| Web API/server | Affected: DI composition and E2E host replacement seam; HTTP routes and response shapes remain unchanged |
| MCP tools | Behavior unchanged; real in-process handlers are exercised by E2E scenarios |
| Console/stdio | Not affected; their current Gemini and MCP composition remains unchanged |
| React frontend | Minimally affected for accessible/stable selectors; no user-visible feature behavior intended |
| Tests | Affected: new E2E project, scripted adapter, host fixture, browser helpers, and focused construction tests |
| Documentation | Affected: active plan, documentation index, final testing reference/ADR, and plan archival |

## Compatibility and migrations

- No EF Core migration or stored-data change is required.
- No HTTP route, DTO, MCP tool name, argument schema, result shape, or authentication contract changes.
- The production factory must preserve the existing Gemini settings and session ownership semantics.
- Test-only service replacement must not be selectable by ordinary production configuration.

## Plan review

Reviewed on 2026-08-02 before implementation:

- Every acceptance criterion maps to a code/configuration change and named test evidence.
- The vertical slice covers browser, HTTP/authentication, planning orchestration, real MCP handlers,
  tenant-aware repositories, and SQLite while replacing only the paid external model boundary.
- Clean Architecture dependency direction remains unchanged: the test assembly references production
  projects, and no production layer references the E2E project or its scripted adapter.
- Tenant identity continues to come from the authenticated principal and established session/context
  accessors; scenario selection cannot choose a database path or user identity.
- HTTP, MCP, database, frontend, console, and stdio contracts remain compatible.
- No migration is required.
- The scripted adapter, scenario registry, prompts, and generated planning data remain inside the
  temporary E2E process and are not logged as production telemetry or exposed through a test endpoint.
- Initial scope is limited to the smallest useful application E2E slice; broader browser matrices,
  live Gemini evaluation, and exhaustive MCP coverage are explicitly deferred.

The plan is implementation-ready with no unresolved product or security decision.

## Verification

Focused checks while implementing:

```sh
dotnet test OwnPlanner.Web.Server.Tests/OwnPlanner.Web.Server.Tests.csproj
dotnet test OwnPlanner.E2E.Tests/OwnPlanner.E2E.Tests.csproj --filter "Category=E2E"
./scripts/verify.sh --frontend
```

Final gate:

```sh
./scripts/verify.sh --all
git diff --check
git status --short
```

The final review will map every acceptance criterion to a named passing test. A skipped browser test,
missing browser binary, unavailable static frontend, or sandbox restriction remains incomplete and
must not be reported as passed.

## Exclusions

- Evaluating whether Gemini chooses the best tool or writes a high-quality answer.
- Calling Gemini during ordinary local, pull-request, or full repository verification.
- Snapshotting natural-language model output.
- Exhaustively testing every MCP tool through the browser.
- Shipping a public test-control endpoint or a configuration switch that enables the scripted model
  in production.
- Adding scheduled live-Gemini checks in the initial implementation. Such checks, if added later,
  require a separate secret-gated job, bounded usage, and nondeterministic assertions.

## Risks and assumptions

- `WebApplicationFactory.UseKestrel` must serve the built React static assets correctly; validate this
  in the fixture spike before building browser helpers.
- The non-development secure-cookie and HTTPS-redirection behavior may require an E2E-only options
  override in the test host. Production cookie behavior must remain unchanged.
- `ChatSessionManager` is singleton-scoped. Tests will use unique users/sessions and initially run
  conservatively until parallel isolation is demonstrated.
- Playwright browser installation adds setup time and platform dependencies; start with Chromium and
  retain failure artifacts to keep diagnosis practical.
- The current `ChatServiceAdapter` owns meaningful Gemini-specific tool-loop behavior that the
  scripted adapter will not execute. This is accepted for the initial application E2E slice and must
  remain visible in coverage documentation.
