# OwnPlanner Copilot Instructions

## Repository overview

OwnPlanner is a layered `.NET 9` solution with multiple entry points:

- `OwnPlanner.Domain`: entities and business rules
- `OwnPlanner.Application`: use-cases, services, DTOs, orchestration
- `OwnPlanner.Infrastructure`: persistence, SQLite, adapters, external integrations
- `OwnPlanner.Web/OwnPlanner.Web.Server`: ASP.NET Core web server and API
- `OwnPlanner.Web/ownplanner.web.client`: React + TypeScript + Vite frontend
- `OwnPlanner.Console`: CLI entry point
- `OwnPlanner.Mcp.StdioApp`: MCP stdio host for tool execution
- `ownplanner.web.client`: React + TypeScript + Vite frontend

## General rules

- Follow existing repository style, naming, and folder conventions.
- Keep changes minimal and scoped to the requested task.
- Do not introduce placeholder comments, TODO-only implementations, or incomplete code.
- Avoid breaking public contracts unless explicitly requested.
- Prefer existing abstractions, helpers, and services over introducing new patterns.
- Preserve nullability annotations and existing async APIs.
- Keep parameter ordering consistent across similar APIs.

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
