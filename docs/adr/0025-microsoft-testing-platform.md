# ADR-0025: Native Microsoft Testing Platform verification

**Date:** 2026-09-22  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

[Issue #47](https://github.com/am-space/own-planner/issues/47) completes the test-runner migration
deferred by [ADR-0017](0017-supported-dependency-baselines.md). The `xunit.v3` 4.x packages use
Microsoft Testing Platform (MTP) v2, whose VSTest bridge is unsupported with the .NET 10 SDK.
Changing packages alone would break verification, filtering, and result reporting.

This decision supersedes ADR-0017's xUnit compatibility exclusion. All its other runtime,
frontend, MCP, SQLite, and OpenAPI decisions are retained by reference.

## Decision

- Select `Microsoft.Testing.Platform` in `global.json` without changing the .NET 10 SDK baseline.
- Use `xunit.v3` 4.0.1 in every test project. This is still the upstream xUnit.net v3 framework;
  the 4.x number is the package version. Its native integration supplies MTP 2.4.0 and works with
  the existing `Microsoft.Playwright.Xunit.v3` package.
- Enable `UseMicrosoftTestingPlatformRunner` and executable output explicitly in every test
  project. Remove `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`; supported execution
  uses native MTP rather than retaining a second runner path.
- Replace existing `coverlet.collector` references with `coverlet.MTP` 10.0.1 to preserve opt-in
  coverage collection through `--coverlet`.
- Preserve all four `verify.sh` modes. Build the solution for backend verification, then run each
  backend test project separately, excluding the E2E and deployment projects. Retain trait
  exclusions for E2E, DeploymentSmoke, and LiveAi within backend projects as well. Do not ignore
  the zero-tests exit code: an unexpectedly empty backend project must fail verification.
- Use MTP's explicit `--project` / `--solution` selectors and xUnit's trait/class/method filters.
  E2E selects only `Category=E2E`; deployment wrappers keep their explicit categories and existing
  authorization and cleanup behavior.
- Use the built-in xUnit TRX reporter. Preserve `TestResults/E2E/e2e.trx` and both deployment
  report names. CI continues to call the same scripts and upload the same E2E artifact directory.

## Consequences

### Positive

- All seven test projects share the supported .NET 10 testing path.
- Existing tests, fixtures, Playwright integration, and cancellation APIs require no source changes.
- Verification still separates deterministic backend/browser checks from opt-in deployment tests.
- Package upgrades no longer depend on the VSTest compatibility bridge.

### Negative / Trade-offs

- Developers must use MTP-compatible IDE support or the CLI. VSTest-specific command arguments
  must be replaced with the examples in [testing.md](../testing.md).
- Backend projects run sequentially to retain strict per-project discovery failures. Adding a
  new opt-in suite requires excluding its project and category in the verification script.

## Alternatives Considered

- **Retain the old packages** — postpones the supported runner migration requested by #47.
- **Ignore exit code 8 for a solution-wide filtered run** — would also mask unexpectedly empty
  backend projects. Dedicated project invocations preserve the stricter failure behavior.
- **Add a separate TRX extension** — unnecessary because xUnit already supplies a TRX reporter.

## Sources

- [xUnit 4.0.1 release notes](https://xunit.net/releases/v3/4.0.1)
- [xUnit MTP integration](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [.NET runner selection](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test)
- [MTP command options](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-mtp)
- [Coverlet MTP integration](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/Coverlet.MTP.Integration.md)

## Related Files

| File | Role |
|---|---|
| `global.json` | Native MTP selection |
| `OwnPlanner.*.Tests/*.csproj` | Test framework, runner, and coverage references |
| `scripts/verify.sh` | Backend and E2E selection and report contract |
| `scripts/docker-smoke-test.sh` | Deployment smoke filter and report |
| `scripts/docker-live-ai-test.sh` | Opt-in live-AI filter and report |
| `docs/testing.md` | Supported commands and runner requirements |
