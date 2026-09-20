> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0020: Request-scoped chat skills](../adr/0020-request-scoped-chat-skills.md).
> Details below may not reflect later refinements made during review.

# Dynamic chat skills (#53)

**Status:** Implemented

Enable curated, request-scoped instructions and tools alongside directly callable agents, with
mode permissions enforced before execution.

## Acceptance and evidence

| Requirement | Change and validation |
|---|---|
| Discovery and four curated skills | Application registry: Notes, Goals and Organization, Weekly Planning, Reflection; catalog and registry tests |
| Actual declarations on next request | Gemini request composition uses active runtime tools and trusted skill instructions; scripted provider tests |
| Direct agents in General | Add the backend General configuration with both agents in its compact baseline; preserve existing enum values and defaults |
| No implicit data fetch | Loading changes only request state; test no underlying MCP calls |
| Reset and deduplication | New runtime per user request; instructions exist only in request system instructions, never tool results/history; test repeated loads and consecutive turns |
| Clear bounded failures and limits | Atomic loads validate permissions and installed definitions; existing round cap includes loads; test failures/cancellation/limit |
| Execution authorization | Snapshot declarations at each model request; calls in a load batch cannot use newly loaded tools until the next model request; test both batch orders and read-only policy |
| Isolation | Instance/request-local state; preserve host MCP adapter binding; test separate conversations and real tenant adapters |

## Impact map

- Application: registry, tool policy, request runtime, mode configuration, planning adapter contract.
- Infrastructure: Gemini tool declarations, trusted request instructions, execution guard, lifecycle.
- Web API / Telegram / console: shared PlanningService path; no new routes or host-specific runtime.
- MCP / stdio: existing tool schemas and handlers reused unchanged; skill_load is a local chat
  capability like agent calls, not a standalone public MCP operation.
- Frontend: additive mode type only if needed to represent the backend contract; selector/defaults
  and starter experience stay in #54.
- Domain / persistence: not affected; no migrations.
- Tests: Application policy, Infrastructure orchestration, tenant-bound adapter integration.
- Docs: AI integration reference, accepted ADR, archived plan, index.

## Implementation order and review

1. Build registry and provider-neutral policy/runtime, following TaskPlanningMcpAdapter's explicit
   allowlists. Configure optional baseline skills and fail closed for read-only writes.
2. Integrate in ChatServiceAdapter, preserving existing shared handlers and mode boundaries.
3. Wire the General backend baseline through PlanningService; leave DayWork as the default.
4. Add deterministic tests, update reference docs and ADR, verify and inspect the complete diff.

Reviewed before implementation: all criteria have behavior/evidence mappings; Application owns
policy; SDK types remain in Infrastructure; tenant selection stays host-owned. No external tool
contracts, existing mode permissions, or stored enum meanings change. General's backend baseline
is included to satisfy #53's direct-agent requirement; default selection, UX and initial report
belong to #54. Skills cannot introduce destructive context/list deletion. Unknown/unavailable
skills load atomically, and tools are authorized against the declarations seen by that model call.

## Verification

Run focused Application, Infrastructure and Web Server tests, then `./scripts/verify.sh --all`.
Run setup if dependencies are missing. Inspect `git diff --check`, all changed files, and acceptance
coverage before delivery. Archive with the accepted ADR in the implementation PR.

## Risks and exclusions

Request-only instructions must not leak into replayed conversation or compaction. Provider
declarations must actually update, rather than only tool text. Existing mode tool sets stay intact.
No downloaded/user-authored skills, arbitrary executable code, new agents, automatic mode changes,
schema migrations, broad report preload or General report implementation.

## Implementation review and evidence

- `ChatSkillRuntimeTests` cover discovery, baseline skills, atomic failures, permission ceilings,
  deduplication, request reset and preserved enum meanings/existing permissions.
- `ChatSkillOrchestrationTests` exercise serialized Gemini HTTP requests and real orchestration:
  actual declaration/instruction updates, direct agents, both batch orders, successive requests,
  recovery/rebuild/mode switching, bounded load errors, round limits and cancellation.
- `PlanningServiceTests` prove General applies the compact baseline without data preloading.
- `DirectToolMcpAdapterTests` verify registered skill references and execute loaded reads and writes
  against real separate user databases; foreign data is not returned or reopened.
- `./scripts/verify.sh --all` passed frontend lint/build, all backend suites and 16 browser tests.
  Final review removed permanent note/goal deletion from the curated skills, fixed new test warnings,
  and added an empty-baseline execution regression. Final `./scripts/verify.sh --backend` passed
  all 552 backend tests with zero build warnings or errors.
- No migrations or existing HTTP/MCP contract changes. General's default, UI and report remain #54.
