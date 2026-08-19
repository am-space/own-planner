# ADR-0013: Deterministic weekly workload reporting

**Date:** 2026-08-19
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

Week Planning previously preloaded broad goal, task-list, and task collections and relied on the
model to join them, distinguish focus plans from deadlines, calculate daily load, and detect gaps.
This consumed context and made the same planner state produce inconsistent weekly conclusions.
[ADR-0010](0010-deterministic-strategic-reporting.md) established focused read models but deliberately
deferred weekly time-window semantics. GitHub issue
[#35](https://github.com/am-space/own-planner/issues/35) defines this feature contract.

## Decision

### 1. Use a weekly-specific Application read contract

`IWeeklyReportReader` returns immutable weekly DTOs rather than extending the strategic report.
Options select an optional UTC start date, bounded task samples, and a bounded overloaded-day
threshold. The report exposes the exact seven-date half-open window and the captured UTC instant.

### 2. Keep focus plans and due commitments distinct

Infrastructure reads incomplete tasks from non-archived task lists through the host-provided
`IPlannerDbContextFactory`. Focus and due counts and samples remain separate in each day. A task in
both views retains one identifier and contributes once to distinct workload counts. Overdue means
`dueAt < asOfUtc`; window membership uses UTC instants within the selected calendar dates.

### 3. Produce bounded deterministic evidence and signals

Tasks sort by overdue status, importance, due date, focus date, and identifier. Descriptions are
loaded only for selected samples and truncated to 200 characters. Context and active-goal summaries
support drill-down, including an explicit bucket for unassigned or missing contexts. Deterministic
signals identify overloaded days, active goals without focused work, overdue carryover not focused
inside the window, and important tasks without a focus date.

### 4. Share one additive MCP tool across hosts

`weekly_report_get` is read-only and idempotent. It accepts an optional strict `yyyy-MM-dd`
`startDate`, a task sample limit (default 3, range 0–5), and overloaded-day threshold (default 5,
range 1–20). Web in-process execution, HTTP MCP, and stdio register the same handler. Week Planning
preloads only this report while retaining existing entity tools for targeted reads and mutations.

## Consequences

### Positive

- Weekly arithmetic and signals are deterministic and tenant-bound.
- The model receives substantially less broad planner data on mode entry.
- UTC and focus-versus-due semantics are explicit and testable.
- Separate weekly and strategic contracts can evolve independently.

### Negative / Trade-offs

- UTC dates may not match a user's local calendar until timezone preferences exist.
- Task-count overload is a coarse capacity proxy because tasks have no duration estimates.
- The report is a point-in-time snapshot and must be refreshed after relevant mutations.

## Alternatives Considered

- **Extend `strategic_report_get` with weekly flags** — rejected because it couples structural and
  time-window contracts.
- **Treat focus dates as deadlines** — rejected because focus is a flexible work plan and due is a
  fixed commitment.
- **Let the model select an overload threshold implicitly** — rejected because signals would no
  longer be deterministic.

## Deferred

User timezones, capacity/duration planning, calendar events, automatic scheduling, historical
reflection, and a reporting UI remain separate features.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Reporting/WeeklyReport.cs` | Weekly options and immutable result contract |
| `OwnPlanner.Infrastructure/Reporting/WeeklyReportReader.cs` | Tenant-bound UTC aggregation |
| `OwnPlanner.Mcp.Tools/WeeklyReportTools.cs` | Shared MCP schema and handler |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Week Planning preload and prompt |
| `docs/ai-integration.md` | User-facing integration semantics |
