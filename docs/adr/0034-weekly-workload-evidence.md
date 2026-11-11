# ADR-0034: Evidence-based weekly workload guidance

**Date:** 2026-11-11

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#78](https://github.com/am-space/own-planner/issues/78) identifies two misleading recommendations:
interpreting a default count of five tasks as established overload, and suggesting due dates for
flexible scheduling. The report introduced by [ADR-0013](0013-deterministic-weekly-workload-reporting.md)
already separates focus plans and deadline commitments, deduplicates their overlap and bounds
samples while preserving exact counts. [ADR-0029](0029-shared-user-calendar.md) supplies its current
calendar semantics. Neither report contains duration or available-time evidence.

This decision clarifies interpretation and conversational planning without replacing the report
contract or calendar decision. The [implementation plan](../archive/weekly-workload-guidance-plan.md)
records scope, acceptance criteria and validation limits.

## Decision

Keep `IsOverloaded`, `OverloadedDays` and `OverloadedDayThreshold`, including the default five,
inclusive comparison and existing JSON/MCP names. The signal remains the distinct union of focused
and due tasks on a day meeting the configured threshold. It measures concentration for review;
it is not proof of overload, infeasibility or available capacity. XML comments, the shared MCP
description and current reference documentation explain this meaning. No fields or capacity
calculation are added, and report selection/counting logic is unchanged.

Week Planning removes suggestions to assign deadlines for scheduling and unsupported claims about
which work will not fit. The existing `weekly_planning` skill carries the detailed evidence policy,
so it also applies when General loads the skill. Use conversation-supplied time, task sizes and
constraints; ask briefly for essential missing available-time/largest-task information. Do not
invent estimates or infer the whole backlog from bounded samples.

Propose a small core and optional reserve when useful, explaining choices from actual deadlines,
importance, goals and user constraints. Discussion and proposals do not authorize writes; concrete
user directions retain immediate execution of the specified changes without redundant confirmation.
Flexible scheduling uses focus dates and preserves deadlines. Due-date changes require genuine
user-supported deadline intent. Existing tools, skill composition, permission ceilings, goal-first
review instructions and shared calendar rules remain in place.

## Consequences

### Positive

- Weekly suggestions state their evidence and limits while preserving external numerical contracts.
- General and Week Planning receive consistent capacity and focus/deadline guidance.
- Existing tenant isolation, report arithmetic and review workflows are reused without migrations.

### Negative / Trade-offs

- Compatibility names retain the word "overloaded"; every consumer must respect the documented
  count-only meaning rather than treating that label as an effort estimate.
- Natural-language interpretation remains model behavior. Policy checks and scripted transport
  tests verify delivered context, declarations and tool routing, not a live model's reasoning.
  Representative model-evaluation cases live in the reference docs; no live-model evaluation is
  claimed by the deterministic test suite.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Week Planning outcomes and focus/deadline policy |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | Shared weekly evidence and capacity guidance |
| `OwnPlanner.Application/Reporting/WeeklyReport.cs` | Documented compatibility signal meaning |
| `OwnPlanner.Mcp.Tools/WeeklyReportTools.cs` | Shared model-facing description |
| `OwnPlanner.Application.Tests/Chat/WeeklyWorkloadPolicyTests.cs` | Delivered instruction and composition checks |
| `OwnPlanner.Infrastructure.Tests/Reporting/WeeklyReportReaderTests.cs` | Inclusive threshold and deduplication coverage |
| `OwnPlanner.Infrastructure.Tests/Adapters/WeeklyWorkloadConversationTests.cs` | Scripted context and authorized routing evidence |
| `docs/ai-integration.md` | Current behavior, evidence limits and representative evaluation cases |
