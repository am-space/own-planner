# ADR-0023: Explicit task deadline clearing

**Date:** 2026-09-21

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

Task partial updates use null to mean unchanged, so a nullable stored deadline could not be removed.
[Issue #61](https://github.com/am-space/own-planner/issues/61) requests explicit clearing following
the existing goal-clearing convention. See the [implementation plan](../archive/clear-task-deadline-plan.md).

## Decision

Add optional `clearDueAt=false` to the shared task-update tool and Application service. Clearing
wins over a supplied date, including an invalid string: the tool skips date parsing on this branch,
and Application calls the existing nullable domain setter. Without clearing, existing parsing,
partial-update and validation behavior remain intact. Focus dates and other fields are independent.

Append the service parameter after `ct` to preserve existing positional cancellation-token callers.
All MCP transports use `TaskItemTools`; there is no separate HTTP task editor or frontend mutation
contract. General task-management instructions and the Task Planning execution prompt describe
explicit clearing and confirmation from the returned task; their existing permission policies apply.

Full task DTOs serialized by all three tool paths retain `dueAt: null`, including subsequent
`taskitem_get` results, to explicitly confirm absent deadlines. Other null fields and compact task
list DTOs retain the existing omission behavior. A shared metadata rule in `TaskToolSerialization`
is applied to the direct adapter and the SDK task-tool registrations for HTTP MCP and stdio.

## Consequences

- Deadline removal is explicit, additive, and requires no schema migration or task recreation.
- Fresh deadline reports exclude cleared tasks while focus-date planning remains available.
- Appending a boolean after the cancellation token is unconventional but preserves source compatibility.
- Full task tool responses include one additional null-valued field when no deadline exists.
- Scripted chat tests exercise orchestration with real persistence; they do not measure live model quality.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Tasks/ITaskItemService.cs` | Partial-update contract |
| `OwnPlanner.Application/Tasks/TaskItemService.cs` | Deadline mutation |
| `OwnPlanner.Mcp.Tools/TaskItemTools.cs` | Shared tool schema and parsing |
| `OwnPlanner.Mcp.Tools/TaskToolSerialization.cs` | Shared explicit-null deadline serialization |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ToolResultJson.cs` | Explicit null confirmation |
| `OwnPlanner.Web.Server.Tests/Services/TaskDeadlineIntegrationTests.cs` | Persistence, reports, scoped access and scripted chat coverage |
