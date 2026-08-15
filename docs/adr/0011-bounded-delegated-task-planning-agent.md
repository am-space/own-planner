# ADR-0011: Bounded delegated task-planning agent

Date: 2026-08-14

Status: Accepted

Deciders: OwnPlanner maintainers

## Context

Global Planning needs to turn strategic intentions into tasks without receiving unrestricted direct
task-write tools. The existing Search Agent demonstrates isolated model sessions, but task planning
also requires authenticated planner reads and carefully bounded mutations. Prompt instructions alone
cannot enforce tool or entity scope.

## Decision

`task_planning_agent_call` is a local Gemini capability exposed only in Global Planning. Every call
starts an isolated specialist session using a trusted prompt, tool allowlist, and fixed round limit.
It receives only the explicit objective and optional context/task-list scope, not the parent history.

The specialist reuses the host-provided `IMcpAdapter`. A provider-neutral restricted adapter in the
Application layer validates scope through authenticated entity reads, rejects disallowed or recursive
tools, constrains scoped queries, verifies every task/list mutation, and records mutation outcomes.
The Gemini adapter owns model-session execution and includes nested usage in the parent turn totals.

The capability is not registered as a public MCP tool. Existing planner MCP schemas and handlers
remain the single business-operation path used by web, console, and stdio-backed chat.

## Consequences

- Global Planning can delegate actionable decomposition without gaining direct task-item writes.
- Tenant isolation remains the responsibility of the authenticated host adapter; delegation adds a
  narrower invocation scope inside that tenant.
- Scoped agents cannot use broad task queries whose results cannot be proven to belong to the scope.
- Future specialist agents can reuse the isolated-session pattern, but must define their own trusted
  policy rather than inheriting this task-specific allowlist.
- Provider cancellation stops OwnPlanner orchestration and tool calls; cancellation of an in-flight
  Gemini HTTP operation is limited by the provider SDK's API.

## Alternatives considered

- Grant direct task-write tools to Global Planning: rejected because it removes the strategic versus
  execution boundary.
- Enforce scope only through the specialist prompt: rejected because prompts are not authorization.
- Copy task business logic into an agent-specific service: rejected because it would diverge from the
  shared MCP tool behavior.
- Filter serialized broad-query results: rejected as brittle; scoped broad queries are constrained or
  rejected instead.

## Related files

| Area | File |
| --- | --- |
| Contracts and policy | `OwnPlanner.Application/Chat/TaskPlanningAgentContracts.cs` |
| Mode exposure | `OwnPlanner.Application/Chat/ModeConfig.cs` |
| Gemini orchestration | `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` |
| Reference documentation | `docs/ai-integration.md` |
