# ADR-0030: Today-first current-week attention in General

**Date:** 2026-10-02\
**Status:** Accepted\
**Deciders:** OwnPlanner maintainers

---

## Context

Broad General attention requests need unfinished focus plans from earlier this week and future
focus-only work alongside today's plan and deadlines. [ADR-0029](0029-shared-user-calendar.md)
already supplies shared periods but its five-task period sample does not supply distinct attention
sections, reason annotations or a way to retrieve every relevant match. Legacy UTC and rolling
report fields are public contracts. The originating plan is preserved in
[the archive](../archive/general-week-attention-plan.md); the issue is
[#74](https://github.com/am-space/own-planner/issues/74).

## Decision

Application's pure `GeneralAttentionBuilder` composes current-state attention from eligible task
metadata and a resolved `thisWeek` period. Today is resolved from that same captured clock and
preferences. Focus dates retain their stored date; deadlines use shared local-day UTC boundaries.
No separate calendar logic or review lifecycle is introduced.

The default overview assigns each task once, in order: today, earlierThisWeek, remainingWeek,
overdue, olderFocus. Every reason and relevant date survives assignment. Section match counts
include overlaps; assigned counts and per-day breakdowns describe the deduplicated overview.
Week day breakdowns stay bounded; older/overdue backlogs have counts without unbounded day arrays.
Default samples are five per section except olderFocus, which remains count-only. Titles are clipped;
no descriptions or note bodies are fetched.

Existing `general_report_get calendarPeriod=thisWeek` gains `calendar.attention`; all earlier fields
retain their definitions. A new shared read-only `general_attention_get` operation accepts section,
offset and limit. Named sections page every matching task, even if its default display was elsewhere.
Pages are fresh current state rather than frozen cursors. The existing General reader supplies the
same tenant-bound eligible projection for both operations.

General declares the query immediately, without skill loading. Instructions require fresh queries
for broad attention and repeated requests, today-only queries for explicit day scope, distinct focus
and deadline labels, bounded output and optional drill-down. An overview authorizes no mutations,
mode switches or weekly-review opening. Specialist permissions remain unchanged.

## Consequences

### Positive

- Broad attention includes unfinished and future focus-only work while keeping today primary.
- Exact counts, reason annotations and section paging support bounded output without losing evidence.
- Application composition and shared host wiring preserve compatibility and tenant isolation.

### Negative / Trade-offs

- Eligible metadata is materialized for exact counts, matching the existing General reader pattern.
- Specific reason queries can repeat tasks from another query; consumers must deduplicate combined data.
- Concurrent changes can shift offset pages; a subsequent request reads the current state.
- Natural-language routing and presentation remain model behavior; scripted tests establish orchestration,
  not real-model quality.

## Alternatives Considered

- Add pagination arguments to the existing General report: rejected to preserve its simple named-period
  contract and avoid fetching unrelated goal/capture information for each attention page.
- Reuse weekly review: rejected because its frozen review identity, last-day targeting and eligibility
  do not match ordinary current-week attention.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Reporting/GeneralAttentionReport.cs` | DTOs, options, pure classification and sampling |
| `OwnPlanner.Infrastructure/Reporting/GeneralReportReader.cs` | Shared eligible tenant projection and fresh reads |
| `OwnPlanner.Mcp.Tools/GeneralReportTools.cs` | Shared additive query and existing report handler |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | General permissions and attention instructions |
| `docs/general-attention.md` | Current contract, presentation and paging reference |
