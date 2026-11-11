> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0035: Evidence-based strategic diagnoses](../adr/0035-strategic-evidence.md).
> Details below may not reflect later refinements made during review.

# Evidence-based strategic diagnoses

**Status:** Implemented — verification complete

**Issue:** [#79](https://github.com/am-space/own-planner/issues/79)

## Outcome

Global Planning and System Analysis distinguish structural observations from supported planning
consequences and useful next steps, preserving their existing write permissions and report contracts.

## Acceptance mapping

| Acceptance | Change | Evidence |
| --- | --- | --- |
| Precise goal/context signals | Shared strategic skill, mode prompts, report XML and MCP description explain incomplete, non-trashed tasks in non-archived lists | Reader fixtures: completed-only vs no links, archived/trashed exclusions, notes-only/new contexts |
| Legitimate unlinked tasks and empty structures | Explain missing/missing-reference goal links as review candidates; consider operational work and intentional setup | Policy assertions and reader fixtures including inactive existing goal links |
| Evidence, impact and next steps | Fact/consequence/check guidance; bounded/truncated samples, targeted reads, freshness, no manufactured issues | Shared policy tests, scripted targeted reads/refresh and no-issues context; reference evaluation cases |
| Global Planning direction/proposals and authorization | Small useful proposals, goal completion/pause as contextual possibilities; exploration is not authorization, explicit edits execute | Scripted proposal then authorized goal update; delivered policy assertions |
| System Analysis read-only | Evidence-backed structural diagnosis, no offers to execute fixes or automatic mode switching | Permission assertions and scripted rejection of goal writes/delegation/skill escalation |
| Stable contracts/composition/isolation | No tool/schema/DTO/selection/tenant changes | Existing suites plus serialized signal names and explicit mode/skill/tool checks |

## Impact map

- Domain: not applicable; no entity changes.
- Application: shared strategic skill, two mode prompts and report contract documentation.
- Infrastructure: selection comments only; deterministic report fixtures and scripted adapter tests.
- MCP: shared description clarification and contract tests; same in-process and stdio handler.
- Web API, console/stdio wiring, frontend: not applicable; consume shared behavior unchanged.
- Tests: Application policy, Infrastructure reader/routing, MCP description/serialization.
- Documentation: current AI reference, ADR-0035, archived implementation plan and index.

## Order and patterns

1. Verify selection rules and review this plan (completed). Follow #78's shared skill, policy,
   scripted-provider and report-test patterns; retain existing strategic tools.
2. Update shared instructions, mode outcomes and contract documentation without changing queries.
3. Add focused deterministic signal/permission/context/routing coverage.
4. Update current reference and ADR; archive this plan with `git mv` and fix index/links.
5. Run focused checks, `./scripts/verify.sh --all`, inspect complete diff, commit/push and open PR.

## Review and compatibility

Reviewed against every issue criterion: no missing reporting field is needed. The current report
cannot distinguish all-completed from never-linked goals or establish inactivity duration. Existing
targeted reads can supply material context. All dependencies remain inward; tenant-bound reads,
HTTP/MCP shapes, tool sets, skill composition and permission ceilings remain unchanged. No migrations,
dependencies, additional logging, new historical tracking, automatic cleanup or scoring are required.
Only strategic behavior changes; unrelated modes remain unchanged except General receiving the same
shared skill when loaded. No material product ambiguity requires user clarification.

## Validation and limits

Use existing xUnit/MTP suites for focused policy, reader and scripted conversation coverage; run full
repository verification before pushing. Dependencies are already prepared and unchanged. Inspect
documentation links and `git diff --check`. Representative conversations must cover completed-only
and never-linked goals, operational tasks, new/notes-only contexts, truncated samples, no significant
issues, authorized Global Planning edits and rejected System Analysis writes. Scripted fixtures
verify delivered policy, evidence and tool routing, not live-model diagnostic quality; record that
limitation and representative manual model-evaluation cases explicitly.

## Completion evidence

- Focused suites passed: 4 Application policy cases, 107 Infrastructure report/adapter cases,
  and 5 MCP cases.
- `./scripts/verify.sh --all` passed frontend lint/build, 967 backend tests and 32 browser E2E tests.
- Documentation links and `git diff --check` passed; the complete diff preserves tool composition,
  contracts, queries and tenant resolution.
- Existing warnings only: frontend bundle size and ASP.NET test-host obsolescence.
- No live-model evaluation was performed; the reference documentation records representative cases
  and clearly distinguishes scripted routing/context checks from model reasoning quality.
