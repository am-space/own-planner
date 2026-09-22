# Testing OwnPlanner

OwnPlanner uses xUnit.net v3 (the `xunit.v3` 4.0.1 package) with Microsoft Testing Platform (MTP)
for its .NET layers, frontend lint/build checks, and a deterministic Playwright browser suite.
The package version is 4.x; the upstream framework and Playwright integration still use the v3 name.

## Standard verification

Install all dependencies, including the Playwright Chromium binary, once:

```sh
./scripts/setup.sh
```

Then use the verification command that matches the change:

```sh
./scripts/verify.sh --frontend  # React lint and production build
./scripts/verify.sh --backend   # .NET build and non-E2E tests
./scripts/verify.sh --e2e       # React build and Playwright E2E tests
./scripts/verify.sh --all       # complete local/CI-equivalent gate
```

The E2E results file is written to `TestResults/E2E/e2e.trx`. On a browser-test failure, a full-page
screenshot, Playwright trace, and current E2E server log are retained in the same gitignored
directory and uploaded by CI. Passing tests discard their traces and screenshots.

## .NET runner and focused tests

`global.json` selects MTP through the .NET 10 SDK. All seven test projects enable
`UseMicrosoftTestingPlatformRunner` and use executable output. VSTest's `Microsoft.NET.Test.Sdk`
and `xunit.runner.visualstudio` packages are no longer required. Use an MTP-capable IDE runner or
the repository's CLI commands; legacy VSTest invocations are unsupported.

```sh
# Project, class, method, and discovery examples (from the repository root)
dotnet test --project OwnPlanner.Application.Tests
dotnet test --project OwnPlanner.Domain.Tests --filter-class "OwnPlanner.Domain.Tests.Tasks.TaskItemTests"
dotnet test --project OwnPlanner.Domain.Tests --filter-method "*Ctor_Valid_SetsProperties"
dotnet test --solution OwnPlanner.sln --list-tests
```

Use `--project` / `--solution` rather than positional paths. MTP uses xUnit's
`--filter-trait`, `--filter-not-trait`, `--filter-class`, and `--filter-method` options instead
of VSTest's `--filter` expressions. Multiple excluded traits follow one `--filter-not-trait`.

The backend phase builds the solution, then runs each root `OwnPlanner.*.Tests` project except
`OwnPlanner.E2E.Tests` and `OwnPlanner.Deployment.Tests`. Each invocation excludes `Category=E2E`,
`Category=DeploymentSmoke`, and `Category=LiveAi`. Running projects separately preserves MTP's
failure for unexpectedly empty test discovery without treating intentionally excluded suites as
errors. New backend projects matching that convention are picked up automatically.

The E2E phase selects only `Category=E2E`. TRX reports use xUnit's built-in
`--report-xunit-trx --report-xunit-trx-filename e2e.trx` with an explicit results directory;
CI's artifact path remains `TestResults/E2E`. Deployment wrappers use the same report options
and retain their existing filenames under `TestResults/Deployment`.

Projects that previously referenced `coverlet.collector` now reference `coverlet.MTP`. To collect
coverage explicitly:

```sh
dotnet test --project OwnPlanner.Domain.Tests --coverlet --results-directory TestResults/Coverage
```

The runner migration preserved discovery of 705 backend cases, 18 E2E cases, and two opt-in
deployment cases. No test-source changes were required. See
[ADR-0025](adr/0025-microsoft-testing-platform.md) for the compatibility decision.

## E2E runtime model

`OwnPlanner.E2E.Tests` runs Chromium through Playwright for .NET and starts the real ASP.NET Core
application with `WebApplicationFactory<Program>` plus Kestrel on an ephemeral loopback port. The
fixture serves the production Vite build from `ownplanner.web.client/dist`.

The suite runs directly on the developer machine or GitHub Actions runner; it does not run inside
the production OwnPlanner Docker image. The image build remains a separate CI check. This keeps the
browser suite fast while still exercising real browser/network behavior rather than an in-memory
HTTP test server.

Every E2E host creates a unique temporary root containing:

- a central authentication SQLite database;
- a directory for per-user planner SQLite databases;
- test-only configuration with logging email and no Gemini API key.

The fixture refuses paths outside that root and deletes the data root when the suite completes.
Tests share the host conservatively, use unique users and independent browser contexts, and do not
run in parallel.

## Deterministic AI boundary

Normal E2E verification never contacts Gemini. Production web composition uses
`IChatAdapterFactory`, whose `GeminiChatAdapterFactory` implementation constructs the existing
`ChatServiceAdapter`. The E2E host replaces that single factory with a scripted implementation.

Everything behind the model boundary remains real: `ChatServiceFactory`, `PlanningService`,
`DirectToolMcpAdapter`, MCP tool handlers, authenticated tenant resolution, repositories, and
SQLite. A test registers a one-use scenario and sends its generated prompt through the browser. The
scenario can return deterministic text, deliberately fail, or invoke the session's real MCP adapter.
Generated prompts make scenarios order-independent and prevent a process-global response queue.

The suite intentionally does not test Gemini's tool selection, natural-language quality, SDK
payload mapping, or the internal Gemini function-call loop. Those behaviors require focused adapter
tests or a separately approved, secret-gated live evaluation with bounded spend; they do not belong
in pull-request verification.

## External deployment verification

`OwnPlanner.Deployment.Tests` is a black-box Playwright suite for an already-running OwnPlanner
deployment. Unlike `OwnPlanner.E2E.Tests`, it does not host the application or replace Gemini. It
uses only the public health endpoint and browser UI, so it validates the built container, static
frontend, cookie authentication, routing, and persistence together.

The tests require `OWNPLANNER_BASE_URL`; without it they skip. Normal `verify.sh` runs exclude the
deployment categories so an inherited environment variable cannot accidentally target a real
deployment.

| Category | External dependency | Purpose |
|---|---|---|
| `DeploymentSmoke` | Running OwnPlanner only | Health, registration, navigation, logout, protected routes |
| `LiveAi` | Running OwnPlanner plus Gemini key | Bounded prompt-to-tool-to-persisted-state evaluation |

Use `scripts/docker-smoke-test.sh` for the default disposable container workflow and
`scripts/docker-live-ai-test.sh` only after explicitly providing `GEMINI_API_KEY`. Failure
screenshots and traces are retained in `TestResults/Deployment/`; the wrapper scripts also retain
container logs on failure before cleanup. See [`docker.md`](docker.md) for commands and secret
handling.

Run `python3 -m unittest discover -s scripts/tests -v` to check verification phase selection,
report arguments, failure propagation, and deployment wrapper isolation with mocked commands.
These regression tests also cover failed container startup and rejection of a live test authorized
only through `.env`; they do not start containers or contact Gemini.

## Current browser coverage

Dependency compatibility checks also run under `Category=E2E`:

- `McpTransportE2eTests` connects an MCP v2 client to the real bearer-authenticated HTTP host and
  to the built stdio process. It checks discovery, representative task schemas and result envelopes,
  explicit null deadlines, Unicode, cross-user isolation, token rejection/revocation, stateless HTTP,
  in-process result parity (excluding intentionally omitted audit timestamps), and persisted reads
  after a stdio restart. The E2E project builds the stdio host through a project dependency.
- `OpenApiE2eTests` starts an isolated Development host and requests `/openapi/v1.json`, checking
  JSON content, the OpenAPI version, and representative existing routes. Production exposure is
  unchanged: this endpoint remains Development-only.
- `DependencyUiE2eTests` exercises registration, chat, Tasks/Goals/Notes searches and inspectors,
  Settings token creation, navigation/assistant controls, and logout in light/dark themes at desktop
  and mobile widths. Long titles must wrap within the inspector and leave its close button usable.

See [dependency-baselines.md](dependency-baselines.md) for supported versions, deferred upgrades,
and repeatable package audit commands.

The initial suite covers:

- unauthenticated protected-route redirection;
- registration, authenticated chat access, logout, and loss of protected access;
- deterministic chat rendering without Gemini credentials;
- task creation and later listing through the real MCP and persistence path after clearing chat;
- per-user task isolation across two independent browser contexts;
- the existing user-facing response to a provider failure.

When adding a scenario, prefer accessible roles and labels, keep external services scripted at their
narrowest boundary, and use real application/database paths inside the temporary tenant environment.
Do not duplicate exhaustive domain, application, or MCP coverage through the browser.
