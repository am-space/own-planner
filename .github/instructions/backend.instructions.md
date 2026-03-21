---
applyTo: "OwnPlanner.Domain/**/*.cs,OwnPlanner.Application/**/*.cs,OwnPlanner.Infrastructure/**/*.cs,OwnPlanner.Web/OwnPlanner.Web.Server/**/*.cs,OwnPlanner.Console/**/*.cs,OwnPlanner.Mcp.StdioApp/**/*.cs,**/*.csproj"
---

# Backend Instructions

## Technology

- Use `C#` for server-side changes.
- Target `.NET 10` and align with the existing code style in the edited project.
- Preserve nullability annotations and existing async APIs.

## Coding rules

- Prefer `async`/`await` for I/O and service operations.
- Preserve cancellation token flow where applicable.
- Use dependency injection instead of direct construction when wiring services.
- Validate public method arguments early.
- Preserve structured logging patterns based on `ILogger<T>` and message templates.
- Avoid sync-over-async unless there is already an established pattern and it cannot be changed safely.
- Dispose async resources safely.

## Layering rules

- Keep domain logic in `OwnPlanner.Domain`.
- Keep application orchestration, use-cases, and DTO mapping in `OwnPlanner.Application`.
- Keep database, filesystem, external API, and adapter concerns in `OwnPlanner.Infrastructure`.
- Keep HTTP, authentication, and presentation concerns in `OwnPlanner.Web.Server`.
- Do not move infrastructure concerns into domain or application layers.

## Web server guidance

- Follow existing ASP.NET Core patterns for controllers, services, and registration.
- Preserve authentication and user-isolation behavior.
- When modifying chat-related services, preserve session behavior, async disposal, and structured logs.
- Prefer extending existing services instead of duplicating cross-cutting logic.

## Change preferences

- Favor small, readable changes over broad refactors.
- Keep public APIs stable unless the task explicitly requires a contract change.
- When editing existing files, match their formatting and local conventions.
