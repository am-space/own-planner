# ADR-0012: Private Telegram presentation channel

**Date:** 2026-08-18
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

Users need private mobile access to the same planner without creating a second orchestration path or
allowing Telegram-supplied identity data to select a tenant. Webhooks can be retried and concurrent,
while a planning turn may already have applied tool mutations before delivery fails.

## Decision

Telegram is an optional web presentation adapter. Authenticated Settings endpoints create and revoke
hashed, expiring, single-use deep-link tokens. `AuthDbContext` owns unique OwnPlanner-user,
Telegram-user, and private-chat mappings plus selected mode and content-free processed update IDs.

`TelegramController` validates the configured webhook secret, filters to new private text messages,
reserves `update_id` before side effects, serializes each chat through an idle-evicted keyed lock,
and atomically advances a persisted per-link update high-water mark so stale turns cannot execute
after newer ones. Deduplication rows use a bounded retention window. It derives the OwnPlanner user
only from the persisted numeric-ID mapping and uses a separate `telegram:<telegram-user-id>` session.
The existing chat session manager, planning service, quotas, Gemini adapter, and tenant-bound direct
tool adapter execute the turn. Failed reserved updates are acknowledged and never automatically
replayed. `TelegramBotClient` sends plain text in Unicode-safe 4,096-character chunks.

Webhook registration is deployment-managed. The application never exposes bot credentials or a
registration operation.

## Consequences

### Positive

- Telegram shares planner behavior, quotas, tools, and tenant isolation with the web channel.
- One-to-one linking and content-free deduplication make identity and retries explicit.
- Account deletion cascades user-owned Telegram metadata; disconnect is independent of planner data.

### Negative / Trade-offs

- Per-chat serialization is process-local, so multi-instance deployment requires a distributed lock
  or chat-affine routing before enabling Telegram on multiple replicas.
- Failed turns are not automatically retried because tool side effects are not transactional with
  Telegram delivery.
- V1 supports only private text and deployment-managed webhooks.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Infrastructure/Telegram/TelegramIntegrationService.cs` | Linking, lookup, mode, and deduplication persistence |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/TelegramController.cs` | HTTP, commands, chat orchestration, and failure semantics |
| `OwnPlanner.Infrastructure/Telegram/TelegramBotClient.cs` | Telegram Bot API transport and response splitting |
| `docs/telegram-integration.md` | Operations and current behavior reference |
