> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0023: Explicit task deadline clearing](../adr/0023-explicit-task-deadline-clearing.md).
> Details below may not reflect later refinements made during review.

# Explicit task deadline clearing

Status: Completed
Issue: [#61](https://github.com/am-space/own-planner/issues/61)

Allow users to explicitly remove a deadline while preserving partial updates and planned focus dates.

## Impact and implementation order

1. Application: append optional `clearDueAt` after the existing cancellation token to preserve positional callers; use the existing nullable domain setter, following `clearGoalId` precedence.
2. MCP: append the optional flag, skip date parsing when clearing, and forward to Application. Shared registration supplies in-process, HTTP MCP and stdio; no new transport.
3. Chat: describe explicit clearing in General's task-management skill and Task Planning instructions, retaining existing write ceilings.
4. Tests and documentation: cover the matrix below; record the contract in reference docs and an ADR, then archive this plan.

Domain and Infrastructure persistence need no implementation changes: deadlines are nullable already. Shared tool serialization for direct, HTTP MCP and stdio preserves explicit null DueAt in full task DTOs so chat receives confirmation; compact list DTOs keep null omission. Web API and frontend have read-only planner surfaces, with no separate update DTO/editor. Console/stdio inherit shared tools. No migration, dependency change, or date/time redesign is required.

## Acceptance evidence

| Criteria | Changes and verification |
|---|---|
| Clear existing/absent deadline; preserve omitted/null; set/replace | Application and tool matrix tests |
| Clear beats valid/invalid dates; invalid ordinary update cannot partially mutate | Tool parsing tests, persistence through real host adapter |
| Preserve title, description, goal, importance, completion, list and focus | Application assertions and persisted integration read |
| Fresh deadline reports exclude cleared task; focus planning remains | SQLite-backed report integration |
| Additive schema and shared host behavior | Direct adapter and MCP SDK schema/handler tests, host builds |
| General scripted chat confirms persisted clearing | Scripted provider with real user-bound tool adapter |
| Scoped execution, proposal/read-only restrictions and tenant isolation | Real adapter integration plus existing policy suites |

## Verification and review

Run focused Application, MCP, Infrastructure and Web Server tests; prepare dependencies with `scripts/setup.sh`, then run `scripts/verify.sh --all`. Inspect the complete diff and `git diff --check` before a pull request targeting master.

Plan review: every criterion has evidence above; business behavior stays in Application, date parsing stays in MCP, existing tenant resolution and permission checks remain in place. Contract additions are an optional boolean and explicit null deadlines in full task tool responses. Assumption: existing empty-string date behavior remains unchanged. No unrelated cleanup or new UI is included.

## Completed validation

- `./scripts/setup.sh` completed; `./scripts/verify.sh --all` passed: 654 backend tests and 17 browser E2E tests, plus frontend lint/build.
- Real stdio and authenticated HTTP MCP smoke checks passed against isolated temporary databases: optional schema flag, clear precedence over null/valid/invalid dates, and subsequent persisted reads. Stdio additionally verified invalid ordinary dates cannot partially update a title.
- Scripted General chat covered successful clearing and missing-task failure using the real user-bound adapter. Existing read-only policies and scoped proposal restrictions passed with the full suite.
- Existing warnings: one moderate npm audit finding, frontend bundle size, and ASP.NET test-host deprecations. No dependencies changed.
- Completed diff and documentation links reviewed; no migration required.
