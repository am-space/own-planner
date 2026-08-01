# MCP Tool Guidance

This project exposes shared MCP tool definitions and handlers used by both the web server's
in-process adapter and the stdio host.

## Implementation

- Implement business behavior in `OwnPlanner.Application`; keep this project focused on stable tool
  schemas, argument parsing, session context, DTO conversion, and handler delegation.
- Treat tool names, descriptions, argument names/types/requiredness, and result shapes as external
  contracts. Prefer additive changes; document and test any intentional incompatibility.
- Do not add user IDs, database paths, credentials, or token material to tool arguments or results.
  User scope belongs to authenticated host wiring; `SessionContext` is host-created diagnostic
  context, not model-supplied authorization input.
- Keep tool definitions and handlers paired, and reuse existing parsing and serialization helpers so
  the in-process and stdio transports behave identically.

## Validation

- Add schema, parsing, success, validation, and application-error coverage in
  `OwnPlanner.Mcp.Tools.Tests`.
- When changing a contract, test exact externally visible names and JSON shapes, plus both host paths
  when transport behavior could differ.
- Run `dotnet test OwnPlanner.Mcp.Tools.Tests/OwnPlanner.Mcp.Tools.Tests.csproj` and build
  `OwnPlanner.Mcp.StdioApp` plus the web server when shared registration or serialization changes.
