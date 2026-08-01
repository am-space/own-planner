# OwnPlanner Validation Matrix

Run the smallest useful checks while iterating, then use the full verification gate before delivery.

## Canonical Commands

Prepare dependencies when required:

```sh
./scripts/setup.sh
```

Run all frontend and backend checks:

```sh
./scripts/verify.sh --all
```

Run one side while iterating:

```sh
./scripts/verify.sh --backend
./scripts/verify.sh --frontend
```

## Change Matrix

| Affected area | Focused feedback during implementation | Final gate |
| --- | --- | --- |
| Domain | Matching Domain test project, filtered class or method where useful | Backend, then all |
| Application | Matching Application tests and directly affected Domain tests | Backend, then all |
| Infrastructure or migration | Infrastructure tests plus affected Application behavior | Backend, then all |
| MCP tool or AI orchestration | MCP Tools tests plus affected Application and Web Server tests | Backend, then all |
| Web API or authentication | Web Server tests plus affected Application/Infrastructure tests | Backend, then all |
| React frontend | Relevant local tests if present, lint, and frontend build | Frontend, then all |
| Cross-layer feature | Focused tests for each slice as it is completed | All |
| Documentation only | Link/path inspection and `git diff --check` | Full build only when the docs affect generated or validated content |

Use repository-standard `dotnet test --filter ...` commands for focused .NET tests. Do not replace the canonical scripts with a second verification workflow.

## Acceptance Review

Before declaring success:

- Map every acceptance criterion to a passing test, a verified interaction, or an explicit manual check.
- Verify error, authorization, empty, and tenant-isolation behavior where applicable.
- Inspect migrations and generated SQL implications when stored data changes.
- Confirm HTTP and MCP schema compatibility when public contracts change.
- Run `git diff --check` and inspect `git status --short` for accidental or generated files.
- Report existing warnings separately from new failures.
- Never describe a skipped or sandbox-blocked check as passed.
