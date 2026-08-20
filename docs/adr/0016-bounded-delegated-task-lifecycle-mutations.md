# ADR-0016: Bounded delegated task lifecycle mutations

Date: 2026-08-20

Status: Accepted

Deciders: OwnPlanner maintainers

## Context

[ADR-0011](0011-bounded-delegated-task-planning-agent.md) restricted the Task Planning Agent to
non-destructive organization because task deletion was irreversible. ADR-0015 changed the shared
`taskitem_delete` operation to move active tasks into recoverable Trash. Users also need the bounded
specialist to complete clearly identified tasks as part of an explicit planning objective.

Prompt policy alone cannot authorize these mutations or enforce authenticated tenant and delegated
entity scope. Permanent deletion still requires direct user confirmation in the authenticated web
Trash flow and is not an MCP operation.

## Decision

The Task Planning Agent's server-owned write allowlist includes `taskitem_complete` and the existing
recoverable `taskitem_delete`. Before forwarding either operation, `TaskPlanningMcpAdapter` resolves
the target task through the host-provided authenticated `IMcpAdapter` and proves that its task list
belongs to any delegated task-list or context scope. Unscoped calls remain confined to the planner
database selected by the authenticated host.

The trusted specialist instruction permits completion or Trash only when the explicit delegated
objective expresses that lifecycle intent and identifies the target sufficiently. It directs the
model to return ambiguous targets as unresolved questions. The hard allowlist continues to prohibit
reopen, restore, permanent deletion, task-list deletion or archival, other archive operations, and
recursive agent calls.

Successful lifecycle writes retain the existing structured action record. Tool errors, missing or
out-of-scope targets, and other rejected operations are warnings and are never reported as actions.
The existing cancellation propagation and tool-call-round bound are unchanged.

## Consequences

- Explicit routine lifecycle work can be completed within the same bounded delegation as task
  organization.
- Recoverable deletion is safe to delegate without granting permanent data destruction.
- Scope checks add authenticated reads before scoped lifecycle writes.
- Natural-language intent and ambiguity are model policy; authorization, tenant isolation, entity
  scope, and the prohibition on stronger operations remain enforced in code.

## Alternatives considered

- Keep all lifecycle mutations manual: rejected because recoverable Trash and existing completion
  can be safely bounded and are routine outcomes of planning conversations.
- Permit permanent deletion with a model confirmation flag: rejected because model-supplied data is
  not proof of user consent.
- Trust the specialist prompt for scope and operation limits: rejected because prompts are not an
  authorization boundary.
- Add agent-specific mutation services: rejected because the shared MCP operations already preserve
  transport behavior and business rules across web, console, and stdio-backed hosts.

## Related files

| Area | File |
| --- | --- |
| Trusted policy and scope enforcement | `OwnPlanner.Application/Chat/TaskPlanningAgentContracts.cs` |
| Specialist instruction and orchestration | `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` |
| Reference documentation | `docs/ai-integration.md` |
