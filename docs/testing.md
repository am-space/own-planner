# Testing OwnPlanner

OwnPlanner uses focused xUnit v3 projects for its .NET layers, frontend lint/build checks, and a
deterministic Playwright browser suite for cross-layer application behavior.

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

## Current browser coverage

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
