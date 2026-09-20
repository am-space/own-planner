> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0022: Shared chat capabilities and bounded proposal delegation](../adr/0022-chat-capabilities-and-delegation.md).
> Details below may not reflect later refinements made during review.

# Chat capabilities and bounded delegation

Status: Implemented. Issue: [#59](https://github.com/am-space/own-planner/issues/59).

Make everyday General task changes direct, compose specialized modes from shared capabilities without permission drift, and support bounded read-only proposals and authorized delegated execution.

## Impact and compatibility

Application: capability groups, six public skills, mode composition, delegation contracts and enforcement.
Infrastructure: chat-local schema parsing, fresh specialist briefs, structured results and scripted provider tests.
Domain, persistence/migrations, HTTP/MCP handlers, console/stdio, Telegram and frontend: no contract or storage changes; review shared wiring and run existing transport tests. Existing enum values and saved modes remain unchanged. MCP operations remain shared and tenant-bound.
Documentation: reference guide, ADR and archived plan; no release/deployment.

## Implementation and acceptance evidence

1. Freeze the merged d704568 specialized permission matrix, including delegated operations; record scripted repeated routine edits and complex-planning counts/timings before changing production code.
2. Introduce shared fine-grained capabilities and task_management. General uses direct tools for requested task edits/lifecycle; weekly owns scheduling guidance and strategic_review owns read-only diagnosis. Test six catalog entries, all lifecycle declarations/execution, no permanent deletion, ambiguity/exploration without writes.
3. Compose specialized permissions from capability groups; use BaselineSkillIds only for wholly permitted skills. Snapshot-test exact ceilings and active declarations, including read-only System Analysis and narrow Day/Reflection writes. Preserve request-local runtime and batch guards; test failures, cancellation, rebuild/recovery and baseline reapplication.
4. Add optional behavior (execution default for old calls; proposal explicitly read-only) and optional brief (constraints, typed entity references, user decisions). Validate size/type/count bounds before any scope lookup/session. Preserve scope, tenant adapter, fresh sessions, cancellation, budgets and non-recursion. Proposal results are structured suggestions separate from factual Actions; never automatically execute them. Test adversarial writes, scope escape, malformed input/output, bounded context and omitted-field compatibility.
5. Script simple/ambiguous/exploratory/week/reflection/research/scoped/diagnostic conversations. Compare routine and complex paths before/after using the same fake provider; measure repeated skill reloads without implying live model speed or intent compliance. Run focused Application/Infrastructure suites and ./scripts/verify.sh --all; inspect complete diff and transport wiring. Write Accepted ADR, update references/index and git mv this plan to archive.

## Risks and assumptions

Permission drift is the primary risk: full public skills require every tool to be permitted, so restricted modes use smaller shared groups. Delegation grants are part of effective permissions, even where direct tools differ. Prompts guide intent and routing; execution policy enforces declared capabilities and proposal read-only behavior, not natural-language intent. Reference IDs in briefs never grant scope. Omitted behavior retains execution for compatibility; new prompts require proposal for exploratory planning. Reject oversized briefs rather than silently dropping constraints. No API key is needed for deterministic tests; live Gemini latency/selection remains unverified without an explicitly supplied key.

## Plan review

Reviewed before implementation: each issue criterion maps above to code/test or transport compatibility checks; no outward dependency, new authorization system, tenant selection, external contract break, migration or unrelated cleanup. Historical mode permissions are captured before refactoring. User authorization covers implementation and the repository PR workflow; no additional product decision is required.

## Completion review and evidence

All implementation steps are complete. Specialized permission snapshots match merged d704568;
General gained direct task operations and the six-skill catalog. Proposal declarations/execution,
bounded brief parsing, structured results, malformed input/output, fresh request state, scope and
tenant tests pass. Shared HTTP/MCP and saved mode identifiers remain unchanged, with no migration.

`./scripts/verify.sh --all` passed frontend lint/build, all backend projects and 17 browser tests.
Final Application/Infrastructure reruns after review refinements passed 287 and 161 tests; with
Domain 35, Web 111 and MCP 30, final backend coverage totals 624 passing tests. A separate temporary
real-stdio chat smoke passed direct lifecycle changes and a scoped proposal write attempt.
The ADR records before/after scripted counts and timing limitations; no live Gemini run occurred.
Existing frontend bundle-size and ASP.NET test-host deprecation warnings remain. Final diff review
confirmed issue-only changes, no deployment, no generated artifacts and no personal data or secrets.
