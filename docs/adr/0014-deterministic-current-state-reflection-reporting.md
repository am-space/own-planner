# ADR-0014: Deterministic current-state reflection reporting

**Date:** 2026-08-19
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

Reflection mode previously preloaded broad goal, note-list, note, and task collections. The model had
to reconstruct a review period, calculate completion and missed-focus metrics, and infer unprocessed
captures. Current storage has useful timestamps but no audit history, so model reconstruction could
also imply past events that the database cannot prove. [ADR-0010](0010-deterministic-strategic-reporting.md)
established focused reports while deliberately separating reflection semantics. GitHub issue
[#36](https://github.com/am-space/own-planner/issues/36) defines this feature contract.

## Decision

### 1. Define a reflection-specific current-state contract

`IReflectionReportReader` returns immutable reflection DTOs for an explicit UTC half-open period.
The report includes the captured `asOfUtc`, period endpoints, interval notation, and a list of known
historical limitations. It does not extend or change the strategic or weekly report contracts.

### 2. Report only facts preserved by the current schema

Completed work uses a task that is currently completed and whose persisted `CompletedAt` falls in
the period. A reopened task has `CompletedAt = null` and therefore is not historical completed work.
Created tasks and created/updated notes use their current timestamps. Missed focus means a currently
incomplete task whose current `FocusAt` falls in the period. Goal links, context assignments, and
Inbox membership are current associations; prior transitions cannot be reconstructed.

### 3. Use the well-known Inbox identity

Unprocessed-note reporting counts notes currently stored in the non-archived note list identified by
`WellKnownIds.InboxNoteList`. It does not infer note types from titles. Samples are bounded, ordered
pinned-first then most-recently-updated, and load at most a 200-character marked content preview.

### 4. Return deterministic summaries, samples, and signals

Completed and missed-focus tasks are grouped by non-archived context and active goal, with an
explicit unresolved-context bucket for legacy associations. Active goals expose completed-period
coverage plus current remaining and overdue counts. Signals identify focused-but-incomplete work,
active goals without completed work, current overdue carryover, and Inbox accumulation. Task samples
use deterministic completion or unresolved-work ordering and bounded description previews.

### 5. Share one additive MCP tool across hosts

`reflection_report_get` is read-only and idempotent. `periodDays` defaults to 7 and accepts 1–31;
`endAtUtc` accepts only ISO timestamps carrying a zero UTC offset; task and note sample limits default
to 3 and accept 0–5. Web in-process execution, HTTP MCP, and stdio register the same handler.
Reflection mode preloads only this report while retaining existing detail and permitted write tools.

## Consequences

### Positive

- Retrospective metrics and limitations are deterministic rather than model-inferred.
- Broad planner collections and unnecessary personal content are removed from mode startup.
- Both MCP delivery paths remain tenant-bound through `IPlannerDbContextFactory`.
- Exact UTC boundaries, Inbox identity, and reopened-task behavior are testable.

### Negative / Trade-offs

- The report cannot show a completion that was later reopened or a note that left Inbox.
- Current goal/context links may differ from their associations at the time work occurred.
- UTC periods may not align with a user's local day until timezone preferences exist.

## Alternatives Considered

- **Infer history from `UpdatedAt`** — rejected because an update does not identify what changed.
- **Infer capture notes from titles** — rejected because the well-known Inbox ID is the only
  first-class unprocessed-note signal.
- **Add event history in this feature** — rejected as a separate persistence and product decision.

## Deferred

Audit history, event sourcing, timezone-aware periods, previous-period comparisons, productivity
scores, generated retrospective notes, and reporting UI remain separate features.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Reporting/ReflectionReport.cs` | Reflection options and immutable result contract |
| `OwnPlanner.Infrastructure/Reporting/ReflectionReportReader.cs` | Tenant-bound current-state aggregation |
| `OwnPlanner.Mcp.Tools/ReflectionReportTools.cs` | Shared MCP schema and handler |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Reflection preload and prompt |
| `docs/ai-integration.md` | Current reflection-report semantics and drill-down guidance |
