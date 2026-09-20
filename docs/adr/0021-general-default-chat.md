# ADR-0021: General default chat with bounded initial context

**Date:** 2026-09-20

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#54](https://github.com/am-space/own-planner/issues/54) makes everyday discussion and action possible
without defaulting every conversation to today's execution. [ADR-0020](0020-request-scoped-chat-skills.md)
introduced the required skill runtime and General enum value; its default/UI/report rollout was deferred.
This decision completes that rollout without replacing its runtime decision or specialized modes.
The [implementation plan](../archive/general-default-chat-plan.md) records the acceptance mapping.

## Decision

General is the Application default, inherited by web/API and console. The React selector exposes
General and uses it for new/reset chats and missing-mode fallbacks while preserving active explicit
selections. New Telegram links start in General; existing saved modes and `/new` selections remain
unchanged. Enum values, database schema, existing MCP contracts and specialized permissions are preserved.

General declares exactly five initial tools: general report, datetime, skill loading, task agent,
and search agent. Only the General report is preloaded. The user determines what to discuss; prompt
instructions distinguish exploration from intent to write, require confirmed results, and direct
fresh targeted reads after changes. Initial context is explicitly a dated snapshot, reused during
compaction without repeated full-report loads. It does not trigger an automatic assistant briefing.

Application owns immutable report DTOs and deterministic composition. Infrastructure projects
metadata from the user-bound factory in a read transaction. The shared parameterless read-only
MCP handler is registered in all hosts. No model argument can choose a tenant or database.

The report uses UTC with explicit calendar boundaries. Focus dates and deadlines remain distinct;
completion counts reflect current state. Incomplete deadline buckets are disjoint: before today,
today, and the seven days beginning tomorrow. Inbox capture counts use current system Inbox note
membership as a proxy for unreviewed captures; unscheduled Inbox tasks have neither focus nor due
date. Trashed tasks and tasks in archived lists are excluded; missing-list tasks remain eligible.
Direction samples active goals linked to today's focus or today's/upcoming incomplete commitments.

Exact counts accompany fixed sample limits and truncation flags. At most five remaining focus IDs
and three nearest-deadline IDs reference a deduplicated table of at most eight task samples; at most
three linked active goals are sampled. Task and goal titles are capped at 80 UTF-16 code units.
Descriptions and note bodies are excluded. Stable date/title orderings end with ID tie-breakers.
[The reference contract](../ai-integration.md#general-initial-context-report) defines all predicates.

## Consequences

- New conversations can discuss ideas before capturing or planning them, without manual mode changes.
- Task creation, update, assignment, focus, importance, completion and recoverable Trash remain
  available through the direct task agent. Weekly Planning skill supplies task/Trash reads, restore,
  and reopen. Permanent deletion and destructive list/context removal remain unavailable in General.
- Output size is bounded; a representative fixture is approximately 600–1,000 tokens by character/4
  estimation, not a guarantee for every model or language. Exact counting reads eligible metadata,
  so query cost still scales with planner size.
- UTC is explicit; no timezone preference subsystem or historical completion data is invented.
- Deterministic report, provider, tenant, transport and browser tests cover the shipped integration.
  Scripted provider tests validate prompt delivery and tool orchestration, not live model compliance.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Reporting/GeneralReport.cs` | Contract, counting and bounded composition |
| `OwnPlanner.Infrastructure/Reporting/GeneralReportReader.cs` | Tenant-bound metadata read |
| `OwnPlanner.Mcp.Tools/GeneralReportTools.cs` | Shared read-only tool |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | General prompt, capabilities and starters |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Default mode and snapshot lifecycle |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/ChatPage.tsx` | New/reset/fallback and explicit mode behavior |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/TelegramController.cs` | General command and persisted choice |
