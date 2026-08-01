# Web Server Guidance

This project owns HTTP presentation, cookie and bearer authentication, per-user session wiring, and
in-process chat/tool orchestration. Keep business rules in Application and persistence in
Infrastructure.

## Authentication and user scope

- Derive the current user from the validated cookie or bearer principal. Never trust a route, query,
  body, header, or MCP argument to choose a user ID or database file.
- Resolve planning data through `PlannerSessionContextAccessor`, `PlannerAppDbContextFactory`, and
  the established per-user initialization flow. Preserve async disposal and session boundaries.
- Keep authentication responses, logs, and exception details free of password hashes, reset tokens,
  personal access tokens, API keys, and other users' personal data.

## HTTP and tool contracts

- Treat controller routes, request/response DTOs, status codes, authentication semantics, and chat
  streaming/tool-result shapes as external contracts. Prefer additive compatible changes.
- Keep MCP execution delegated to the shared `OwnPlanner.Mcp.Tools` handlers through
  `DirectToolMcpAdapter`; do not create a web-only copy of tool behavior.
- Preserve structured logging, cancellation flow, and temporary export-file cleanup.

## Validation

- Put controller, authentication, session, and adapter tests in `OwnPlanner.Web.Server.Tests`.
- For authentication or data-resolution changes, cover unauthenticated access and two-user isolation.
- Run `dotnet test OwnPlanner.Web.Server.Tests/OwnPlanner.Web.Server.Tests.csproj`; also run MCP tool
  tests when shared schemas, arguments, or serialization are affected.
