# ADR-0040: Occurrence-aware report evidence

**Date:** 2026-11-13

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#93](https://github.com/am-space/own-planner/issues/93), stage 5 of #84, requires reports and chat
mode preloads to represent computed recurring work. Existing report counters and GUID samples
address stored tasks. Reusing a task ID for several occurrences would lose identity, and treating
series closure as completion would invent activity. Stage 4's [ADR-0039](0039-unified-recurrence-reads.md)
already supplies shared projection and independent fact reads. The overall
[implementation plan](../recurring-tasks-plan.md) remains active through stage 8.

## Decision

Add `ReportWork` to General (UTC and requested local calendar), Day Work, General attention,
Week Planning, Reflection and strategic reports. Preserve legacy stored-task fields and GUID
samples. Reflection completed/missed-focus counts describe ordinary tasks; recurring-parent
closure is excluded from all completed-work counts, including General's manual-focus summary.
Ordinary selection and representative dates use the original report range even when occurrence
generation expands earlier. The shared ordinary projection preserves focus-in-range precedence,
then local-deadline selection, so older focus dates cannot hide in-window commitments.

`Pending`, `Scheduled` and `CompletionActivity` are separate, overlapping selections with explicit
calendar/UTC bounds. Pending includes unresolved retained work without a lookback cap and earlier
unfinished work in overlapping current periods. Scheduled unions computed effective rows with
independent recorded facts, giving the original composite identity precedence once. Activity uses
actual half-open UTC completion instants for ordinary tasks and facts. Closed/predecessor facts
remain available outside shortened generation bounds. There is no inference that old work satisfies
a successor occurrence.

Expose exact ordinary/series/occurrence/source/work totals, work-status counts and before-window
counts, independently of bounded samples. Each in-window day has the same units. Weekly, reflection and strategic reports also expose
goal/context groups. Compact General, attention and Day views omit these groups, preserving a
payload bound independent of workspace group cardinality.
Sample titles are clipped to 80 characters without splitting a surrogate pair; descriptions are
absent. Source deadlines are deduplicated outside occurrence samples. Only requested-window dates
produce daily sections, even when retained work spans decades. Zero sample limits preserve evidence.

Extract a visitor from `PlanningWorkReader` so paging and report aggregation share selection,
effective status and ordering. Infrastructure binds the visitor to the report's existing tenant
transaction and resolved calendar; no second database snapshot, reconciliation, clock read or
write is introduced. Report source queries omit description columns and select only list IDs and
context IDs. Exact aggregation may scan every retained date, but never materializes pending rows.

Weekly no-focused-work observations use pending scheduled occurrence counts. Reflection
no-completed-work observations use actual activity. Strategic structural counts retain one source
per series and the evidence limits of [ADR-0035](0035-strategic-evidence.md). Repeated work counts
are not estimates of duration, capacity or overload.

Existing shared report handlers serialize the additive evidence across hosts. Tool descriptions
and mode prompts share interpretation guidance. All six preloads use report results, preserving
composite identities. No routes, tool arguments or mutation permissions change. Existing report
section/offset paging continues on stored-task sections; the additive work block is a bounded
overview with explicit truncation.

## Consequences

- Ordinary report contracts remain compatible while mixed work gets explicit identities and units.
- Friday scheduled outcomes and Monday completion activity remain distinct and correctly labelled;
  corrections refresh current facts without counting parent closure.
- Goal evidence is independent of sampling; no recurrence arithmetic is duplicated in report readers.
- Metadata and preferences describe current state. This does not reconstruct historic assignments.
- Exact retained-prefix counting costs CPU and the report transaction can delay concurrent writers.
  Stored facts and source metadata are loaded in memory; generated rows remain streamed.
- Report payload size is bounded per sample/group/date, following existing report group structure.
  Group cardinality still follows current tasks/goals/contexts.

## Deferred

Weekly Review workflow/actions (#94), public recurrence query/action tools (#95), and browser
integration/final plan archival (#96). Public series creation stays disabled.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/Reporting/ReportWork.cs` | Shared aggregate, sample identities, count units and interpretation guidance |
| `OwnPlanner.Application/Tasks/Recurrence/PlanningWorkReader.cs` | Projection visitor reused by pages and reports |
| `OwnPlanner.Infrastructure/Reporting/ReportWorkReader.cs` | Tenant transaction and bounded ordinary-completion adapter |
| `OwnPlanner.Infrastructure/Reporting/*ReportReader.cs` | Report windows, evidence attachment and goal observations |
| `OwnPlanner.Mcp.Tools/*ReportTools.cs` | Shared report transport descriptions |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Interpretation guidance for mode preload context |
