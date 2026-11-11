> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0034: Evidence-based weekly workload guidance](../adr/0034-weekly-workload-evidence.md).
> Details below may not reflect later refinements made during review.

# Evidence-based weekly workload guidance

**Status:** Completed
**Issue:** [#78](https://github.com/am-space/own-planner/issues/78)

## Outcome

Use weekly task counts as concentration signals and help users propose realistic work from known
constraints, preserving flexible focus dates, genuine deadlines and explicit mutation authority.

## Acceptance mapping

| Requirement | Change | Evidence |
| --- | --- | --- |
| Counts do not prove overload/feasibility | Shared weekly_planning guidance and Week Planning wording; document legacy signal names | Policy/instruction tests and captured provider requests |
| Unknown time/task sizes are explicit; reuse supplied constraints | Use conversation capacity and size evidence; briefly ask only for essential missing information | Scripted short/large/missing-capacity scenarios and multi-turn context checks |
| Small core and optional reserve grounded in deadlines/importance/goals/constraints | Shared skill describes proposals and rationale | Policy tests and representative conversation fixtures |
| Focus plans remain distinct from deadlines | Schedule flexible work with FocusAt; DueAt requires genuine user-supported deadline intent | Scripted exploratory versus authorized focus scheduling and explicit deadline update |
| Numerical report contract preserved | Keep IsOverloaded, overloadedDays and threshold names/values; clarify XML/MCP descriptions | Existing report/tool tests plus inclusive default-threshold and deduplication boundary cases |
| Permissions and review behavior preserved | Keep all tool sets, skills, preloads and review workflow instructions | Existing permission snapshots/review tests; baseline and dynamic skill composition checks |
| Calendar follows #75 | Reuse calendarPeriod and existing shared resolver; no new date arithmetic | Existing calendar tests and scripted named-period refresh |

## Impact map

- Application: Week Planning prompt, shared weekly skill, report contract documentation and policy tests.
- Infrastructure: report-reader explanatory comment only; report boundary tests and scripted provider
  orchestration tests (reuse ChatSkillOrchestrationTests helpers).
- MCP: clarify existing weekly_report_get description; unchanged name/schema/result and host paths.
- Documentation: current weekly-planning behavior/limitations, ADR-0034, archived plan and index.
- Domain, persistence/migrations, HTTP APIs, console/stdio wiring and React: not applicable; shared
  instructions and tool description reach existing transports without changing their plumbing.

## Order and patterns

1. Clarify report XML and tool-description semantics without new fields or arithmetic changes.
2. Add shared capacity guidance to weekly_planning, retaining the goal-first review instructions;
   remove conflicting Week Planning suggestions to invent deadlines or assume infeasibility.
3. Add proportional policy checks, inclusive threshold/deduplication fixtures, and scripted
   exploratory/authorized routing using the existing test provider and RecordingMcpAdapter.
4. Update reference docs with representative conversation checks, record the additional decision
   in ADR-0034, and archive this plan in the implementation PR.

## Verification and limitations

Run affected Application, Infrastructure and MCP tests during iteration, then
`./scripts/verify.sh --all` before pushing a PR targeting master with Closes #78. Dependencies are
already prepared; rerun setup if preparation/dependency state changes. Check docs links and full diff.
Instruction tests assert the policy delivered to the model; scripted responses prove context/tool
routing and do not evaluate a live model's reasoning or obedience. Report any live-model evaluation
not performed explicitly; document representative manual checks for short/large tasks, missing and
supplied capacity, and scheduling without real deadlines.

## Exclusions and risks

- No durations/effort fields, availability integration, capacity engine, automatic scheduling,
  new tools/skills or user-calendar implementation.
- The existing external overload terminology stays for compatibility; guidance must state its
  exact numerical meaning and evidence limits rather than silently renaming/redefining fields.
- Default five is a deterministic count threshold, not five tasks of assumed equal size. Bounded
  samples cannot establish the full task set or provide invented duration estimates.
- Existing concrete requests still authorize their specified changes without extra confirmation;
  planning questions and recommendations alone do not. This remains instruction-level policy,
  not a new deterministic natural-language authorization classifier.

## Plan review

Reviewed before implementation: every acceptance criterion has a change and evidence path. The
smallest scope is instructions and contract clarification with no schema or selection-rule changes.
Existing Clean Architecture, tenant resolution, transports, calendar behavior and permission ceilings
remain intact. Report tests protect the inclusive threshold and overlap counts; scripted fixtures are
explicitly distinguished from model evaluation. No secrets, migrations, unrelated mode changes or
material unresolved product decisions are introduced.

## Implementation evidence

- Updated the shared skill, Week Planning prompt, report XML, reader comment and MCP description;
  existing report arithmetic, field names and permissions remain unchanged.
- Focused policy tests passed (3 cases), report/orchestration tests passed (99 cases), and MCP tests
  passed (7 cases). A pre-existing whole-instruction assertion now decodes provider text before
  comparison, preserving the check for multiline instructions.
- Added inclusive threshold/deduplication boundaries, legacy serialized signal names and scripted
  short/large/missing-capacity context, General skill loading, exploratory reads and authorized
  focus/deadline routing. These fixtures do not claim live-model behavior evaluation.
- Full repository verification and remaining model-evaluation limitations are reported in the PR.
