# ADR-0010: Deterministic strategic reporting through focused read models

**Date:** 2026-08-14
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

Global Planning and System Analysis preloaded several broad MCP list results. Gemini then joined
entities, calculated health metrics, and selected relevant examples inside the context window. That
approach consumed substantial context, produced inconsistent totals, and encouraged loading more
personal content than a strategic diagnosis needs. Weekly and reflection reporting will require
different time-window semantics, so one mode-driven report contract would couple unrelated use
cases.

GitHub issue [#34](https://github.com/am-space/own-planner/issues/34) defines the originating feature
contract.

## Decision

### 1. Define a focused Application read contract

`IStrategicReportReader` returns immutable strategic-report DTOs and accepts bounded task and note
sample limits. The contract is specific to the current strategic snapshot. Future weekly and
reflection reports will receive their own focused reader interfaces and may share internal
projection or preview helpers where their semantics match.

### 2. Calculate metrics deterministically in Infrastructure

`StrategicReportReader` uses the host-provided `IPlannerDbContextFactory` and one injected
`TimeProvider` instant. It projects the planning entities needed for counts, summaries, structural
signals, and bounded samples. Archived lists and completed tasks are excluded from active metrics;
ordering and preview truncation are deterministic. The reader cannot select a user or database.

### 3. Expose one additive MCP tool through every host

`strategic_report_get` is a read-only, idempotent MCP tool with optional bounded sample limits. Its
handler validates arguments and delegates to the Application reader. The web in-process adapter,
HTTP MCP server, and stdio server use the same tool class. Cancellation tokens are supplied by the
host and omitted from the model-visible argument schema.

### 4. Preload the report and retain drill-down tools

Global Planning and System Analysis preload only the strategic report. Their existing entity tools
remain allowed so the model can retrieve full details when a signal warrants investigation. The
report returns exact identifiers but prompts continue instructing the model not to show identifiers
in user-facing prose unless requested.

## Consequences

### Positive

- Strategic counts and signals no longer depend on LLM arithmetic.
- Bounded deterministic samples reduce context use and personal-content exposure.
- Both MCP delivery paths share one contract and tenant-bound implementation.
- Focused reader interfaces let weekly and reflection semantics evolve independently.

### Negative / Trade-offs

- The reader performs several cross-entity projections and combines them in memory; query shape and
  payload bounds require continued performance coverage as databases grow.
- A strategic preload is a point-in-time snapshot and can become stale during a conversation; the
  model must call the tool again when current state matters.
- Separate report interfaces create more types than a single generic reporting service.

## Alternatives Considered

- **Generic `IReportingService` with report type or mode flags** — rejected because it weakens
  contracts and couples strategic, weekly, and retrospective time semantics.
- **Continue preloading entity lists** — rejected because the model would still calculate metrics
  inconsistently and consume broad payloads.
- **Materialized or cached reports** — deferred because current data volume does not justify stored
  report state or invalidation complexity.

## Deferred

Weekly windows, reflection periods, historical trends, caching, exports, dashboards, and generated
analysis remain separate features.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Reporting/` | Strategic report contract and immutable DTOs |
| `OwnPlanner.Infrastructure/Reporting/StrategicReportReader.cs` | Tenant-bound deterministic aggregation |
| `OwnPlanner.Mcp.Tools/StrategicReportTools.cs` | Additive MCP schema and handler |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Strategic-mode preload and tool allowlists |
| `docs/ai-integration.md` | Current report semantics and tool workflow |
