# OwnPlanner documentation

This index separates current system documentation from proposed work and historical records.
Documentation maintenance rules are in [`AGENTS.md`](AGENTS.md).

## Current system

These reference documents describe how OwnPlanner works now and should be updated with the code they
describe.

| Document | Purpose |
|---|---|
| [`architecture-layers.md`](architecture-layers.md) | Project boundaries and dependency direction |
| [`ai-integration.md`](ai-integration.md) | Gemini chat orchestration and MCP tool execution |
| [`database-schema.md`](database-schema.md) | Central authentication and per-user database model |
| [`email-configuration.md`](email-configuration.md) | Transactional email configuration and deliverability |
| [`docker.md`](docker.md) | Container build and operating notes |
| [`testing.md`](testing.md) | Local, CI, and deterministic browser E2E verification |
| [`planner-workspace.md`](planner-workspace.md) | Read-only planner UI, HTTP contract, and tenant-safe read path |

## Plans

Active implementation plans live directly in `docs/` as `*-plan.md`:

No implementation plans are currently active.

Potential work without an implementation commitment lives in `backlog/`:

- [`backlog/knowledge-core-plan.md`](backlog/knowledge-core-plan.md) — durable Markdown knowledge and revision history
- [`backlog/knowledge-retrieval-plan.md`](backlog/knowledge-retrieval-plan.md) — search, relations, and review workflows
- [`backlog/knowledge-migration-plan.md`](backlog/knowledge-migration-plan.md) — import, export, and portability

When backlog work becomes active, move its plan into `docs/`. When it ships, record the implemented
decision in an ADR and move the original plan into `archive/`.

## Architecture decision records

ADRs describe decisions as actually shipped. Start new records from [`adr/template.md`](adr/template.md).

- [`ADR-0001`](adr/0001-deferred-planner-dbcontext-factory.md) — deferred per-user `AppDbContext` creation
- [`ADR-0002`](adr/0002-chat-context-management.md) — chat context management
- [`ADR-0003`](adr/0003-email-sending-password-reset.md) — outbound email and password reset
- [`ADR-0004`](adr/0004-task-list-token-reduction.md) — task-list token reduction
- [`ADR-0005`](adr/0005-mcp-wire-non-ascii-escaping.md) — MCP non-ASCII wire escaping
- [`ADR-0006`](adr/0006-gdpr-account-export.md) — GDPR account export
- [`ADR-0007`](adr/0007-gdpr-account-deletion.md) — GDPR account deletion
- [`ADR-0008`](adr/0008-deterministic-browser-e2e-testing.md) — deterministic browser E2E testing
- [`ADR-0009`](adr/0009-read-only-planner-workspace.md) — read-only planner workspace alongside persistent chat

## Historical plans

Archived plans preserve the original intent but may differ from the final implementation. Prefer the
linked ADR when determining current behavior.

- [`archive/email-sending-plan.md`](archive/email-sending-plan.md)
- [`archive/task-list-token-reduction-plan.md`](archive/task-list-token-reduction-plan.md)
- [`archive/e2e-testing-plan.md`](archive/e2e-testing-plan.md)
- [`archive/read-only-planner-workspace-plan.md`](archive/read-only-planner-workspace-plan.md)

## Supporting records

- [`upstream-issue-mcp-wire-encoder.md`](upstream-issue-mcp-wire-encoder.md) — prepared upstream MCP SDK issue and reproduction details
