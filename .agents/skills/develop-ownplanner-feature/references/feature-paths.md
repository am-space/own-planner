# OwnPlanner Feature Paths

Read only the sections relevant to the feature impact map. Always follow the nearest scoped `AGENTS.md` and existing code over this routing summary.

## Domain and Application

Use `OwnPlanner.Domain` for entities, value objects, and business invariants that must hold independently of delivery and persistence.

Use `OwnPlanner.Application` for use cases, orchestration, DTOs, and dependency interfaces. Group work with the existing feature folders. Add XML documentation when adding or changing interfaces.

Keep both layers independent of EF Core, ASP.NET Core, Gemini SDKs, MCP transports, and React concerns.

## Persistence and External Services

Use `OwnPlanner.Infrastructure` for EF Core mappings, context implementations, migrations, repositories, and external SDK adapters.

Choose the database context deliberately:

- Use `AppDbContext` for per-user planning data.
- Use `AuthDbContext` for central account, credential, session, and user-to-database mapping data.

Preserve per-user database isolation. Generate migrations with the EF CLI and specify the context explicitly. Update `docs/database-schema.md` when the resulting schema changes.

## Web API

Use `OwnPlanner.Web/OwnPlanner.Web.Server` for authenticated HTTP endpoints, request/response mapping, DI wiring, and web orchestration.

Resolve data through the authenticated planner session and tenant-aware context path. Keep endpoint logic thin and preserve established status codes and response shapes unless the issue explicitly changes the contract.

## MCP and AI Access

Implement reusable behavior in Application first. Define AI-facing schemas and thin handlers in `OwnPlanner.Mcp.Tools`.

For an AI-accessible feature, verify both delivery paths:

- in-process web execution through `DirectToolMcpAdapter` against the authenticated user's database;
- MCP stdio exposure through `OwnPlanner.Mcp.StdioApp`.

Keep tool names and schemas stable, validate tool inputs, avoid sensitive output, and update `docs/ai-integration.md` when the tool surface or orchestration changes.

Do not create an MCP tool merely because a related feature exists. Add it only when the acceptance criteria require AI access.

## Console and Stdio Presentation

Update `OwnPlanner.Console` or `OwnPlanner.Mcp.StdioApp` only when their user flow, wiring, or transport exposure changes. Reuse the same Application behavior rather than duplicating the use case.

## React Frontend

Use `OwnPlanner.Web/ownplanner.web.client` for user interaction, API integration, and presentation state. Follow the existing React, TypeScript, Vite, and MUI patterns.

Keep business invariants on the backend. Treat frontend validation as user feedback rather than the only enforcement. Account for loading, empty, error, authorization, and narrow-screen states when applicable.

## Documentation Decisions

- Update living reference docs when shipped behavior or architecture changes.
- Create an ADR from `docs/adr/template.md` for a durable architectural decision and make it land with `Status: Accepted` when the implementation pull request merges.
- When a new ADR replaces an earlier decision, mark the earlier ADR as `Superseded by ADR-NNNN` and link the two records.
- Keep speculative or inactive knowledge in `docs/backlog/`.
- Archive completed plans in the implementation pull request through `git mv` and add the banner and ADR link required by `docs/AGENTS.md`; never leave a shipped plan active on `master`.
- Avoid a documentation change when existing reference docs already describe the resulting behavior accurately.
