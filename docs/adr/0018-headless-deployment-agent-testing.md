# ADR-0018: Headless deployment and agent testing

**Date:** 2026-08-29
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

The deterministic browser suite validates application behavior through an in-process Kestrel host,
but it intentionally does not test the production Docker artifact or real Gemini tool selection. A
local or CI server may have no graphical desktop, and live provider checks must not become an
implicit, secret-dependent pull-request gate.

## Decision

- Provide a loopback-only Compose deployment using disposable named SQLite and log volumes.
- Run external deployment checks through headless Playwright against `OWNPLANNER_BASE_URL`; no
  desktop session, display server, or browser window is required.
- Keep deterministic `DeploymentSmoke` coverage separate from the in-process E2E suite. It verifies
  health, registration, cookie-authenticated navigation, logout, and protected-route behavior
  against the built container.
- Keep `LiveAi` coverage separately gated by both `GEMINI_API_KEY` and
  `OWNPLANNER_RUN_LIVE_AI=true`. It uses a unique account and task title, makes one bounded request,
  and grades persisted planner state rather than exact model wording. The wrapper defaults to the
  stable `gemini-3.5-flash-lite` model while allowing an explicit `GEMINI_MODEL` override for model
  comparisons.
- Exclude both external deployment categories from the canonical backend and E2E gates. Wrapper
  scripts start the target, collect failure evidence, and remove disposable volumes.
- Never put provider keys in images, tracked configuration, logs, test names, or command arguments.

## Consequences

### Positive

- Coding agents and headless servers can verify the same Docker artifact users run.
- Deterministic deployment regressions remain distinguishable from provider variability.
- Live AI evaluation proves the complete prompt-to-tool-to-SQLite path with bounded API usage.
- Failure screenshots, Playwright traces, and container logs provide actionable evidence.

### Negative / Trade-offs

- Container smoke testing is slower than the in-process browser suite and requires Docker daemon
  access.
- The local Compose profile uses the Development environment to support loopback HTTP cookies and
  must not be exposed as a production configuration.
- A live Gemini result can vary or fail because of provider availability; it remains opt-in and is
  not a merge gate.

## Alternatives Considered

- **Use only the existing E2E suite** — rejected because it does not exercise the Docker image and
  replaces the model boundary.
- **Require a desktop browser** — rejected because Playwright headless mode provides the necessary
  rendering, interaction, screenshot, and trace capabilities on servers.
- **Run live AI on every verification** — rejected because it would introduce secrets, cost, and
  nondeterminism into the normal development gate.

## Related Files

| File | Role |
|---|---|
| `compose.yaml` | Loopback-only disposable OwnPlanner deployment |
| `OwnPlanner.Deployment.Tests/` | External headless smoke and live-AI scenarios |
| `scripts/docker-*.sh` | Lifecycle, evidence collection, and cleanup |
| `.env.example` | Secret-free local configuration template |
| `docs/docker.md` | Operator workflow |
| `docs/testing.md` | Test-layer boundaries and categories |
