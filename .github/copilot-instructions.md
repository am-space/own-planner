# OwnPlanner Copilot Instructions

`AGENTS.md` is the canonical source for shared repository guidance. Keep this Copilot-specific view
aligned when repository-wide conventions change. Read the closest nested `AGENTS.md` before
changing Infrastructure, MCP tools, the web server, the web client, or documentation; it contains
the component's implementation and validation rules.

## Repository overview

OwnPlanner is a layered `.NET 10` solution with multiple entry points:

- `OwnPlanner.Domain`: entities and business rules
- `OwnPlanner.Application`: use-cases, services, DTOs, orchestration
- `OwnPlanner.Infrastructure`: persistence, SQLite, adapters, external integrations
- `OwnPlanner.Web/OwnPlanner.Web.Server`: ASP.NET Core web server and API
- `OwnPlanner.Web/ownplanner.web.client`: React + TypeScript + Vite frontend
- `OwnPlanner.Console`: CLI entry point
- `OwnPlanner.Mcp.StdioApp`: MCP stdio host for tool execution

## Architecture documentation

For more details on the system's design, please refer to the following documents:
- `docs/architecture-layers.md`: Details the 4-layer clean architecture.
- `docs/ai-integration.md`: Explains the chat workflow and MCP tool execution.
- `docs/database-schema.md`: Details the SQLite separation scheme.

## General rules

- Follow existing repository style, naming, and folder conventions.
- Keep changes minimal and scoped to the requested task.
- Never commit directly to `master`; create a dedicated task branch first. Unless the user specifies
  another name, use `feature/<short-description>` for enhancements and `fix/<short-description>`
  for bug fixes.
- Keep unrelated staged or working-tree changes out of task commits.
- Do not introduce placeholder comments, TODO-only implementations, or incomplete code.
- Avoid breaking public contracts unless explicitly requested.
- Prefer existing abstractions, helpers, and services over introducing new patterns.
- Preserve nullability annotations and existing async APIs.
- Keep parameter ordering consistent across similar APIs.
- When adding a new interface or modifying an existing one, add or update XML documentation comments for the interface and its members to explain intent, contracts, and usage expectations.

## Database Migrations

- Always create EF Core migrations using the dotnet ef CLI tool, never manually. When adding a migration, explicitly specify the DbContext:
  - For `AppDbContext`: `dotnet ef migrations add <MigrationName> --project OwnPlanner.Infrastructure --context AppDbContext`
  - For `AuthDbContext`: `dotnet ef migrations add <MigrationName> --project OwnPlanner.Infrastructure --context AuthDbContext`
  - If the startup project cannot be inferred, append `--startup-project <StartupProjectPath>` as needed.

## Code review rules

- Preserve tenant isolation: resolve planning data from the authenticated session through a
  user-bound `AppDbContext`, never from a client-supplied user ID, database path, or tool argument.
- Treat HTTP routes/DTOs and MCP tool names/schemas/results as external contracts. Prefer additive
  compatibility; intentional breaking changes need migration guidance and affected-transport tests.
- Never log or return password hashes, reset tokens, personal-access-token material, API keys, or
  another user's data. Use ownership-scoped queries, explicit response/export allowlists, redaction,
  and cleanup of temporary export files.

## Testing and validation

- Prefer adding or updating tests only when they are directly relevant to the requested change.
- Keep unit tests close to the affected layer:
  - domain behavior in `OwnPlanner.Domain.Tests`
  - application behavior in `OwnPlanner.Application.Tests`
  - infrastructure behavior in `OwnPlanner.Infrastructure.Tests`
- After code changes, verify the affected project or solution still builds when practical.

## Change preferences

- Favor small, readable changes over broad refactors.
- Keep logging structured.
- Keep public APIs stable unless the task explicitly requires a contract change.
- When editing existing files, match their formatting and local conventions.
