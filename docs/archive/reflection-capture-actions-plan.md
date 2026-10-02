> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0032: Narrow capture actions in Reflection](../adr/0032-reflection-capture-actions.md).
> Details below may not reflect later refinements made during review.

# Reflection capture actions plan

Status: Implemented. Issue: [#76](https://github.com/am-space/own-planner/issues/76).

Enable explicitly requested task creation and note movement from captures in Reflection, preserving source notes and its narrow permission ceiling.

## Acceptance mapping

| Criterion | Change and evidence |
| --- | --- |
| Two actions immediately available, existing workflow preserved | Add exactly taskitem_create and noteitem_assign directly in ModeConfig; frozen permission snapshot and runtime/preload tests |
| Explicit intent, clear target/destination, no redundant confirmation | Reflection instructions and scripted exploratory/authorized/ambiguous conversations |
| Task Inbox default, source note preserved | Resolve existing Inbox list with tasklist_all includeUnassigned=true; create without moving/deleting note; scripted argument/call assertions and existing handler/service behavior |
| Explicit note movement only | Separate requested action through existing noteitem_assign; no broad notes/task capability additions |
| Both requested actions, confirmed partial results, uncertain creation | Instructions forbid atomic-conversion claims, misleading processing completion and automatic uncertain retries; scripted success/error/uncertainty result propagation |
| No extra task writes, skill or changed report | Exact denied-operation runtime and provider execution tests; retain reflection skill/preload/calendar settings |
| Compatible tools and tenant binding | No handler, schema, DI or data access edits; reuse existing shared transports and validation/isolation tests in full verification |

## Impact and order

Application mode instructions/composition and matching tests are affected. Infrastructure adapter production code, Domain, persistence, Web API, MCP handlers, console/stdio, frontend and migrations are not applicable: reuse the existing execution path. Add proportional scripted orchestration tests to Infrastructure.Tests using existing provider/recording adapter fixtures and one real-handler check in Web.Server.Tests. Document the narrow workflow, accepted ADR-0032, and archive this plan.

1. Review this plan distinctly, then update mode instructions and exactly two direct declarations.
2. Update the frozen Reflection ceiling and existing denied-tool fixture; add immediate activation/remaining-denials and scripted workflow checks.
3. Run focused Application/Infrastructure checks, inspect diff and reference links, then ./scripts/verify.sh --all before push. Dependencies are prepared and unchanged.
4. Record shipped permission decision, archive plan with git mv, commit/push and open a PR closing #76.

## Scope and risks

No broad task_management skill, scheduling/completion/assignment/Trash/recovery/deletion grants, calendar or report changes, new MCP tools, or atomic/idempotent conversion. Natural-language intent, clear destinations and result phrasing remain model instructions. Scripted provider responses verify declaration, execution and result propagation rather than live Gemini judgment. Existing task creation requires a list ID, so use the tenant-bound tasklist_all includeUnassigned=true result to select the system Inbox unless the user selects another list. Uncertain task creation is reported without an automatic retry.

## Plan review

Reviewed before feature edits: every criterion maps to mode behavior and evidence; exactly two direct tools are added with the public reflection skill unchanged. Existing Application services and shared MCP handlers own mutations and tenant-bound reads. No schema, host wiring or report edits are required. Source-note preservation and independent results are explicit rather than an atomic conversion abstraction. Add one targeted real-handler integration check for Inbox creation, source preservation and failed/cross-user note movement, alongside scripted instruction/policy coverage. No material ambiguity or unrelated cleanup remains.

Implementation refinement: the real-handler check confirmed list defaults hide unassigned lists.
Use the existing includeUnassigned=true option for task Inbox and note destination resolution;
preserve the external default/contract and test the actual lookup.

## Verification and final review

Full ./scripts/verify.sh --all passed: frontend lint/build, 939 backend tests (Application 391,
Domain 39, Infrastructure 285, MCP 91, Web Server 133), and 26 browser E2E tests, with no skips.
Focused policy tests (2), scripted orchestration suite (83 before the lookup refinement), and the
real-handler integration check also passed. Final full verification includes includeUnassigned=true
lookup assertions and the real task/note list resolution. Existing Vite bundle-size and ASP.NET
test-host deprecation warnings remain. Final diff and documentation links reviewed; exactly two
Reflection direct permissions were added with no new tool, skill, schema, dependency or migration.
