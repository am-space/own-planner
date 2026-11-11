# ADR-0035: Evidence-based strategic diagnoses

**Date:** 2026-11-11

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#79](https://github.com/am-space/own-planner/issues/79) identifies categorical diagnoses in strategic
instructions that exceed the report's evidence. An active goal without incomplete linked work may
have completed tasks; a context without incomplete tasks may contain reference notes. Missing goal
links and empty structures can be intentional. The snapshot does not establish inactivity duration
or severity. [ADR-0010](0010-deterministic-strategic-reporting.md) remains the reporting architecture;
this decision clarifies interpretation without replacing its contract. The
[implementation plan](../archive/strategic-evidence-plan.md) records scope and verification.

## Decision

Keep all strategic report fields, selection rules, limits and tool schemas. Document "active tasks"
as incomplete, non-trashed tasks in non-archived lists. Goals are active and contexts non-archived.
An existing inactive goal reference does not count as missing. Empty-context signals consider
non-archived lists. XML comments, the shared MCP tool description and reference documentation state
these limits without introducing history, new scores or reporting fields.

The shared `strategic_review` skill connects substantive findings to observed facts, possible
planning consequences and concrete checks/next steps. Prioritize demonstrated impact, keep unknown
consequences as requiring review, and allow no significant findings. Operational tasks and empty
structures do not automatically require correction. Targeted reads supply material context;
completed work requires appropriate reads with `includeCompleted=true`, and page/sample/truncation
limits must be respected. Obtain fresh evidence when the dated snapshot cannot support a current
claim. Do not infer historical activity or transitions.

Global Planning uses this evidence to help choose priorities and direction, with a small set of
useful proposals where appropriate. Exploration does not authorize writes; concrete user requests
retain execution through separately permitted capabilities without redundant confirmation.
System Analysis diagnoses structure and possible inconsistencies while remaining read-only. It
does not offer to execute fixes or switch modes automatically. General receives the same evidence
policy when loading the existing strategic skill. Tool sets, skill composition, mode ceilings,
tenant isolation and shared in-process/stdio implementation remain unchanged.

## Consequences

### Positive

- Strategic advice distinguishes structural facts from unsupported defect and severity claims.
- Both strategic modes share evidence rules while retaining distinct outcomes and permissions.
- No migrations, new tools, historical tracking or compatibility changes are needed.

### Negative / Trade-offs

- Compatibility names still say "active tasks" and require the documented selected-scope meaning.
- Material drill-down may require paginated reads; the compact report intentionally cannot explain
  every empty structure or distinguish completed-only from never-linked goals by itself.
- Deterministic tests verify signal definitions and scripted tests verify delivered context and
  permission/routing behavior. They do not prove live-model diagnostic quality. Representative
  model-evaluation cases are recorded in the reference docs; no live-model evaluation is claimed.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Chat/ChatSkillRegistry.cs` | Shared strategic evidence and authorization guidance |
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Distinct Global Planning and System Analysis outcomes |
| `OwnPlanner.Application/Reporting/StrategicReport.cs` | Snapshot, selection and signal documentation |
| `OwnPlanner.Infrastructure/Reporting/StrategicReportReader.cs` | Unchanged deterministic signal selection |
| `OwnPlanner.Mcp.Tools/StrategicReportTools.cs` | Shared model-facing description |
| `OwnPlanner.Application.Tests/Chat/StrategicEvidencePolicyTests.cs` | Instruction delivery and permission composition |
| `OwnPlanner.Infrastructure.Tests/Reporting/StrategicReportReaderTests.cs` | Completed-only, notes-only and operational fixtures |
| `OwnPlanner.Infrastructure.Tests/Adapters/StrategicEvidenceConversationTests.cs` | Scripted evidence, freshness, proposals and denied writes |
| `OwnPlanner.Mcp.Tools.Tests/StrategicReportToolsTests.cs` | Stable signal names, count/sample limits and tool description |
| `docs/ai-integration.md` | Current behavior and representative evaluation cases |
