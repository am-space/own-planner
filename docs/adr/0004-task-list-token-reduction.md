# ADR-0004: Task List Token Reduction — Pagination + Slim Projection

**Date:** 2026-06-21  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

The `taskitem_list_*` MCP tools returned the entire matching task collection in a single,
unpaginated response. On a real account this produced **113 tasks ≈ 73,293 characters** serialized as
one JSON payload — over the MCP tool-output token limit, so the host spilled it to a file and the list
never reached the model's context. From the model's perspective the list was unreadable.

A captured response from the web `/mcp` endpoint confirmed three compounding causes:

1. **No pagination / full description per item.** 113 full-body tasks in one call — the root cause.
2. **Per-item overhead.** Every item carried `createdAt` *and* `updatedAt` (tick precision) plus
   three GUIDs.
3. **Non-ASCII escaped as `\uXXXX`.** 5,313 escape sequences (~26.5k wasted chars); each Cyrillic
   character cost 6 chars instead of ~2 UTF-8 bytes.

Constraints: changes had to be path-agnostic where possible (three distinct tool-output surfaces — the
in-process Gemini chat via `ToolResultJson`, the web `/mcp` endpoint, and the stdio host), preserve
the web/UI and `taskitem_get` full-fidelity DTOs, and follow existing conventions (the note tools
already truncate list content).

## Decision

### 1. Pagination with a deterministic order (Domain + Infrastructure + Application)

`ITaskItemRepository` gained paged methods that count the filtered set and return one ordered page,
with ordering and `Skip/Take` executed in the database. The order is a **total order** so offset
paging never skips or repeats: `FocusAt` ascending with **NULLS-last** (`OrderBy(t => t.FocusAt ==
null)` leading key) → `UpdatedAt` descending → `Id` ascending tiebreaker. Filters (`includeCompleted`,
`onlyImportant`) are applied in the query so page counts are correct.

`PagedResult<T>` (`OwnPlanner.Application/Common`) carries `Items`, `TotalCount`, `Offset`, `Limit`,
and a computed `HasMore`. The service exposes paged overloads that **clamp `limit` to `[1, 100]`**
(default 25) and floor `offset` at 0. The pre-existing non-paged list methods were kept (still used by
tests and a valid repository capability).

### 2. Slim, path-agnostic projection (Mcp.Tools)

The list tools project each item to a new `TaskItemListDto` that **omits `createdAt`/`updatedAt`** and
carries a **truncated description preview** (200 chars + `"… [truncated — call taskitem_get for full
description]"`, mirroring `NoteItemTools`). Results are wrapped in a `{ items, totalCount, offset,
limit, hasMore }` envelope, and each tool's `[Description]` tells the model to page and to call
`taskitem_get` for full text. Because this lives in the tool layer, causes #1 and #2 are fixed on
**all three surfaces** at once; `taskitem_get` and the web/UI keep the full `TaskItemDto`.

### 3. UTF-8 encoder where it reaches the wire (cause #3, partial)

`ToolResultJson.Options` (the in-process chat serializer) sets
`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. This is the **only** surface where the encoder
takes effect (see Consequences). The two MCP hosts keep plain `.WithTools<T>()`.

### 4. SDK bump

`ModelContextProtocol*` packages were moved to **1.4.0** (with `Microsoft.Extensions.Logging.Abstractions`
to 10.0.7 to satisfy it). The bump is retained but, as it turns out, does not change the escaping
behavior.

## Consequences

### Positive

- **Bounded payload regardless of task count** — a default page is 25 slim items. This resolves the
  original "exceeds the limit / spills to a file" failure: the model gets a readable page and can
  request more via `offset`.
- **Smaller per-item size** — no audit timestamps, truncated descriptions.
- **Stable paging** — the total-order sort guarantees no skipped/duplicated rows across pages.
- **UI/`taskitem_get` unaffected** — slimming is confined to the list tool projection.
- **UTF-8 on the chat path** — `ToolResultJson` now emits literal non-ASCII.
- **Covered by tests** — repository ordering (NULLS-last, tiebreaker, no skip/repeat), service
  clamping + `HasMore`, tool envelope/slim-shape/truncation, and a Cyrillic-UTF-8 serializer test.
- **Verified end-to-end** by driving the real stdio MCP host (initialize + `tools/call`).

### Negative / Trade-offs

- **UTF-8 is not achievable on the MCP wire** with the current SDK. The transport serializes JSON-RPC
  messages with the frozen `McpJsonUtilities.DefaultOptions` (ASCII-escaping), which is not
  configurable. So on the web `/mcp` and stdio surfaces, non-ASCII stays `\uXXXX`-escaped, and a
  Cyrillic-heavy 25-item page is ~37k chars — bounded, but above the ~10k that the chat path achieves.
  This was confirmed exhaustively (see Alternatives).
- **200-char description preview kept** (not dropped). A decision to favor giving the model some
  description context over the smaller payload; revisit if MCP pages run hot.
- **SDK bump is unrelated churn** — kept for currency, but it delivered none of the intended UTF-8
  benefit.
- **Two list method families** — paged + non-paged coexist on the repository/service.

## Alternatives Considered

### UTF-8 on the MCP wire — every avenue, all rejected (verified)

- **Per-tool `WithTools(options)` encoder** — 1.1.0 crashed at startup (read-only options need a
  `TypeInfoResolver`); 1.4.0 booted but the wire stayed escaped. Per-tool options govern stage-1
  (result→content), not the stage-2 transport writer.
- **`ConfigureJsonOptions(...)`** — does not exist in any released SDK (≤ 1.4.0, the latest).
- **ASP.NET `ConfigureHttpJsonOptions`** — wrong pipeline; MCP doesn't serialize via Minimal-API JSON.
- **Hand-serializing JSON in the tool** — strictly worse; the returned string is re-escaped as a JSON
  value (every `"` becomes `"`).
- **Reflection** — mutating the singleton's encoder in place: no effect (writer options cached at
  first use); replacing the static instance: CLR-blocked (`initonly` static field).
- **PR [csharp-sdk#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925)** — adds
  `McpServerOptions.JsonSerializerOptions`, but its diff threads options only into
  `McpServerTool/Prompt/Resource.Create` (stage 1). It would not fix stage-2 wire escaping even once
  released. Issue [#636](https://github.com/modelcontextprotocol/csharp-sdk/issues/636) is a stage-1
  concern (`NumberHandling`), which is why per-tool options solve *it* but not *us*.

### Description handling

- **Drop description from the list entirely** (~6k/page, matches the source analysis) and **short
  ~80-char preview** were both considered; **kept the 200-char preview** by decision.

## Deferred

- **UTF-8 on the MCP wire.** Blocked on the SDK exposing a configurable serializer for *transport
  message* serialization (no issue/PR currently proposes this — #925 is stage-1 only). A ready-to-file
  upstream issue is drafted at `docs/upstream-issue-mcp-wire-encoder.md`. If/when fixed, set the
  transport encoder to `UnsafeRelaxedJsonEscaping` and revisit the preview length to reach <10k/page
  for non-Latin data.
- **Date precision.** Reducing `dueAt`/`focusAt`/`completedAt` to day precision is a smaller, separate
  win; do it in the tool projection if pages still run hot.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Domain/Tasks/ITaskItemRepository.cs` | Paged method signatures |
| `OwnPlanner.Infrastructure/Repositories/TaskItemRepository.cs` | DB-side deterministic order + paging + count |
| `OwnPlanner.Application/Common/PagedResult.cs` | Paged envelope record |
| `OwnPlanner.Application/Tasks/ITaskItemService.cs` + `TaskItemService.cs` | Paged overloads, limit/offset clamping |
| `OwnPlanner.Application/Tasks/TaskItemListDto.cs` | Slim list shape (no audit timestamps, preview description) |
| `OwnPlanner.Mcp.Tools/TaskItemTools.cs` | `limit`/`offset` params, envelope, slim projection + truncation |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ToolResultJson.cs` | `UnsafeRelaxedJsonEscaping` encoder (chat path) |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs`, `OwnPlanner.Mcp.StdioApp/Program.cs` | Plain `.WithTools<T>()` + note on the SDK serializer constraint |
| `*.csproj` (×5) | `ModelContextProtocol*` → 1.4.0; `Logging.Abstractions` → 10.0.7 |
| `docs/archive/task-list-token-reduction-plan.md` | Original implementation plan (archived) |
| `docs/upstream-issue-mcp-wire-encoder.md` | Prepared upstream issue for the wire-encoder gap |
