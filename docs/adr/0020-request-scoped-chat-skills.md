# ADR-0020: Request-scoped chat skills alongside direct agents

**Date:** 2026-09-20

**Status:** Superseded by ADR-0022

**Deciders:** OwnPlanner maintainers

## Context

[#53](https://github.com/am-space/own-planner/issues/53) requires compact initial capabilities that
can expand for the user's current request without hiding existing agents behind skill loading.
Mode declaration filtering alone did not enforce execution permissions. Skill instructions must
expire together with their tools, including after cancellation, compaction and session recovery.
The [implementation plan](../archive/dynamic-chat-skills-plan.md) records the acceptance mapping.

## Decision

Application owns the immutable curated `ChatSkillRegistry`, `ChatToolPolicy`, and a fresh
`ChatSkillRuntime` for each user request. The registry covers Notes, Goals and Organization, Weekly
Planning, and Reflection, referencing existing shared tool names. Skills contain trusted static
instructions and no executable code, data access or host identity.

Mode policy separates baseline declarations, the permission ceiling, allowed skill identifiers,
and optional baseline skills. The backend General enum value is appended, preserving persisted
values. It starts with datetime, skill loading, and both existing agents. Day Work remains the
default and other modes retain their tool sets. General's default selection, UI and initial report
are deferred to #54.

Infrastructure registers `skill_load` locally, like the delegated agent capabilities. Loads validate
all referenced capabilities before activation. Actual Gemini declarations and trusted system
instructions are recomposed for every model request. Only bounded load status enters function
results, so skill instructions do not persist in conversation or compaction history. Repeated loads
and overlapping tools are deduplicated. Baseline skills, when configured, are restored each turn.

Execution checks the active mode's policy, installed declarations, and the tool set visible to the
model before its response. A load cannot authorize a sibling call in the same batch. An explicit
read allowlist prevents write capabilities in read-only policy even if accidentally configured.
Skill calls use the existing tool-round budget and cancellation flow. SDK-wrapped cancellation is
translated back to cancellation at the chat boundary.

Planner execution remains on the host's existing tenant-bound MCP adapter; no transport handler,
database selector, schema or migration changes. Skills and agents are peers: supported task
mutations use the task agent directly, while weekly planning exposes targeted task reads, restore
and reopen. Permanent entity deletion is excluded from General.

## Consequences

- Initial General tool declarations remain compact while capabilities expand within the same turn.
- Execution guards cover existing specialized modes as well as dynamic skills.
- Request state cannot cross users or conversations. Deterministic HTTP-level provider tests verify
  declaration/instruction updates, lifecycle, agent calls, batching, limits and cancellation. Real
  host tests verify tenant isolation for loaded reads and writes.
- A skill with any missing or denied tool fails as a whole; it does not silently become a partial
  skill. Read-only variants require an explicitly curated definition if needed in the future.
- Skills may need to be loaded again on successive user messages. This deliberately bounds active
  instructions and tools instead of growing session capabilities indefinitely.
- `IChatAdapter` implementations must accept the new host-owned tool-policy configuration.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | Curated instructions and existing tool references |
| `OwnPlanner.Application/Chat/ChatToolPolicy.cs` | Mode permission and request lifecycle rules |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | General baseline and optional skill configuration |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Mode policy wiring shared by chat hosts |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Gemini declarations, trusted instructions and execution checks |
| `OwnPlanner.Infrastructure.Tests/Adapters/ChatSkillOrchestrationTests.cs` | Scripted HTTP provider integration evidence |
| `OwnPlanner.Web.Server.Tests/Services/DirectToolMcpAdapterTests.cs` | Shared registration and authenticated tenant evidence |
