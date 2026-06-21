# OwnPlanner — Task List Token Reduction (pagination, UTF-8, slim projection)

## Context

`taskitem_list_*` tools return the full task collection in a single unpaginated response. On a real
account this produced **113 tasks = 73,293 characters (~20k+ tokens)** as one JSON payload, which
exceeds the MCP tool-output token limit — the host spilled it to a file and the list never reached
the model's context. (Source analysis: `docs/improvement.md`.)

A captured response from the web MCP endpoint (`taskitem_list_items`, 113 objects, single line,
73,293 chars) confirms all three causes on that surface: **5,313 `\uXXXX` escapes**, **both
`createdAt` and `updatedAt` present in every item at tick precision** (`…28.2177879`), and full
descriptions inline. Nulls *are* omitted, so this is the MCP SDK serializer — **not** `ToolResultJson`.

Three compounding causes, with measured impact:

1. **Non-ASCII escaped as `\uXXXX`.** Every Cyrillic character is emitted as a 6-char `З`
   sequence instead of ~2 UTF-8 bytes — 5,313 sequences ≈ 26.5k wasted chars. UTF-8 output alone:
   73,293 → 46,789 (−36%).
2. **Per-item overhead.** Each task carries `createdAt` + `updatedAt` at tick precision plus three
   GUIDs. Dropping `updatedAt` and reducing dates to day precision: → 40,153 (−45% vs. original).
3. **No pagination, full description per item.** 113 full-body tasks in one call is the root cause.
   A list view needs id + title + status flags; full text belongs in `taskitem_get`.

**Target:** a typical page **bounded regardless of task count**. On the in-process chat surface (with
the UTF-8 encoder) a page is well under 10k chars; on the MCP surfaces, escaping is unavoidable
(see Key facts) so a 25-item Cyrillic-heavy page is ~37k chars — large but bounded, which is what
resolves the original "exceeds the limit / spills to a file" failure. (Decision: keep the 200-char
description preview rather than dropping it; revisit if MCP pages still run hot.)

### Key codebase facts that shape this plan

- **Three tool-output surfaces, not one.** Tool results are serialized differently depending on caller:
  1. **In-process Gemini chat** → `DirectToolMcpAdapter.cs:77` → `ToolResultJson.Options`. Already
     drops `createdAt`/`updatedAt` and omits nulls (ADR-0002), but sets **no UTF-8 encoder**, so
     Cyrillic is still `\uXXXX`-escaped.
  2. **Web MCP HTTP endpoint** → `app.MapMcp("/mcp")` + `.AddMcpServer().WithHttpTransport().WithTools<…>()`
     (`Program.cs:191/254`, `ModelContextProtocol.AspNetCore` 1.1.0). Uses the **MCP SDK serializer**:
     camelCase + omit-nulls, but **keeps timestamps and ASCII-escapes**. **This produced the 73k file.**
  3. **Stdio MCP host** → `OwnPlanner.Mcp.StdioApp/Program.cs` `.AddMcpServer().WithTools<…>()`. Same
     MCP SDK serializer as #2.
  So `ToolResultJson` covers only surface #1; the SDK serializer behind #2 and #3 trims nothing but nulls.
- **The MCP transport encoder cannot be changed (verified).** On surfaces #2/#3 the SDK serializes
  every outgoing message through the static `McpJsonUtilities.DefaultOptions` singleton, which
  ASCII-escapes non-ASCII. It is get-only, frozen before `Main` runs, and `McpServerOptions` exposes
  no serializer hook; per-tool `WithTools(options)` does **not** reach the wire. Confirmed empirically
  by driving the stdio host on both `ModelContextProtocol` **1.1.0 and 1.4.0** — the wire stayed
  `\uXXXX`-escaped either way. **Cause #1 (UTF-8) is therefore only fixable on surface #1.**
- **Tool-layer projection is the path-agnostic lever — prefer it.** Pagination, description
  truncation, *and* dropping `createdAt`/`updatedAt` can all be done in the tool projection
  (`TaskItemTools` returning a slim list shape), which every surface goes through. That fixes causes
  #2 and #3 everywhere in one place, instead of replicating timestamp-drop logic across three
  serializers. The **UTF-8 encoder (cause #1) is a serializer concern** and is applied only where it
  reaches the wire — `ToolResultJson` (surface #1). The MCP surfaces (#2/#3) keep escaping; their
  payload is bounded by pagination + the slim projection instead.
- **Note truncation precedent.** `NoteItemTools` already truncates list content via a
  `note with { Content = preview }` projection (`ContentPreviewMaxLength = 200`,
  `TruncationSuffix = "… [truncated — call noteitem_get for full content]"`). Mirror it for tasks.
- **Affected tools:** `taskitem_list_items`, `taskitem_list_by_goal`, `taskitem_list_by_focus_date`
  (the last is day-bounded but should stay consistent).
- **Current ordering is in-memory and non-deterministic for paging:** repositories do
  `query.ToListAsync()` then `OrderByDescending(t => t.UpdatedAt)` (no DB-side order, no Skip/Take).
- **`TaskItemDto`** carries `Description`, `CreatedAt`, `UpdatedAt`, plus `Id`/`TaskListId`/`GoalId`
  GUIDs. `TaskItem` domain fields: `FocusAt` (nullable), `IsImportant`, `IsCompleted`, etc.

## Decisions

- **Ship the deterministic default sort first; expose an optional `sort` param later.**
- **Default sort:** `FocusAt` ascending with **NULLS LAST** (overdue/earliest planned first, backlog
  not dominating the top) → `UpdatedAt` descending → `Id` ascending as a total-order tiebreaker so
  offset paging never skips/repeats.
- **`isImportant` stays out of the default sort** (product decision; conflicts with chronological
  order). `onlyImportant` remains a filter. Completed tasks stay excluded by default
  (`includeCompleted=false`).
- **Pagination is offset-based** (`limit` default 25, hard max 100; `offset` default 0), returning a
  paged envelope so the model knows to continue.

## Build order

### 1. Slim + deterministic repository queries (Infrastructure)

In `TaskItemRepository`, move ordering into the query and add DB-side paging. EF/SQLite NULLS-LAST
via an `OrderBy(t => t.FocusAt == null)` leading key:

```csharp
query.OrderBy(t => t.FocusAt == null)        // false (has date) before true (null)
     .ThenBy(t => t.FocusAt)
     .ThenByDescending(t => t.UpdatedAt)
     .ThenBy(t => t.Id);
```

Add a paged list method returning items + total count (one `CountAsync` + one `Skip/Take` query),
e.g. `Task<(IReadOnlyList<TaskItem> Items, int TotalCount)> ListPagedAsync(TaskListFilter filter, int offset, int limit, ct)`.
Apply the same ordering to `ListByTaskList`/`ListByGoal`/`ListByFocusDate` so paging is stable
everywhere.

### 2. Service: paged result + DTO unchanged at the edges (Application)

Add a `PagedResult<T>` record (`IReadOnlyList<T> Items, int TotalCount, int Offset, int Limit, bool HasMore`)
in `OwnPlanner.Application`. Add paged overloads to `ITaskItemService`
(`ListAsync`/`ListByTaskListAsync`/`ListByGoalAsync`/`ListByFocusDateAsync` gain `offset`/`limit`).
Clamp `limit` to `[1,100]`, `offset` to `>=0` in the service. `TaskItemDto` is unchanged here — the
**description preview is applied in the tool layer**, so the web/UI and `taskitem_get` keep full text.

### 3. Tool layer: paging params, envelope, description truncation (Mcp.Tools)

In `TaskItemTools`, mirror `NoteItemTools`:
- Add `int limit = 25, int offset = 0` params to the three list tools; pass through to the service.
- Return a paged envelope `{ items, totalCount, offset, limit, hasMore }`.
- **Project items to a slim list shape** rather than reusing `TaskItemDto`. Introduce a
  `TaskItemListDto` (or project to an anonymous/record shape in the tool) that **omits `createdAt`
  and `updatedAt`** and carries a **truncated description** — constants
  `DescriptionPreviewMaxLength = 200`, suffix `"… [truncated — call taskitem_get for full description]"`.
  Doing the timestamp-drop here (not in a serializer) fixes cause #2 on **all three surfaces** at
  once; `taskitem_get` and the web/UI keep the full `TaskItemDto`.
- Update each tool's `[Description(...)]` to state the page defaults and that description is a
  preview (so the model knows to page and to call `taskitem_get` for full text), matching the
  note-tool wording.

### 4. UTF-8 output where it reaches the wire (cause #1)

Set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` on **`ToolResultJson.Options`** — the
in-process chat path (#1). This is verified to produce literal UTF-8.

**The MCP surfaces (#2/#3) cannot be fixed this way** — they serialize through the SDK's frozen
`McpJsonUtilities.DefaultOptions` singleton (get-only, frozen before `Main`, no `McpServerOptions`
hook; per-tool `WithTools(options)` does not reach the wire). This was attempted with per-tool
options on both hosts and **rejected**: on 1.1.0 it crashed startup (`JsonSerializerOptions ...
read-only`, needs a `TypeInfoResolver`), and on 1.4.0 it booted but the wire stayed escaped. Both MCP
hosts therefore keep plain `.WithTools<T>()` (the SDK default already does camelCase + null-omission);
their payload is bounded by pagination + the slim projection. The `ModelContextProtocol*` packages
were bumped to **1.4.0** (with `Microsoft.Extensions.Logging.Abstractions` to 10.0.7 to satisfy it);
the upgrade is retained but does not change the escaping behavior.

> Note: `UnsafeRelaxedJsonEscaping` is the documented, intended option for non-HTML transport (it
> still escapes control chars). The in-process chat result is JSON, not HTML, so this is safe.

### 5. (Optional, follow-up) Date precision

Reducing `dueAt`/`focusAt`/`completedAt` to day precision is a smaller, separate win and changes what
the model sees. Defer unless measurements after steps 1–4 still run hot; if done, do it in the tool
projection, not the DTO.

### 6. (Deferred — blocked on SDK, no upstream fix yet) UTF-8 on the MCP wire

Cause #1 cannot be fixed on the MCP surfaces today, and **no current upstream change fixes it**.
The escaping happens in the SDK's *transport/session message serializer* (**stage 2**) — the final
JSON-RPC write, done with the frozen `McpJsonUtilities.DefaultOptions` (ASCII-escaping). Everything
configurable in the SDK operates at *result→content serialization* (**stage 1**): per-tool
`WithTools(options)`, and the proposed server-wide `McpServerOptions.JsonSerializerOptions` in PR
[csharp-sdk#925](https://github.com/modelcontextprotocol/csharp-sdk/pull/925) (its diff only threads
options into `McpServerTool/Prompt/Resource.Create`). Issue
[csharp-sdk#636](https://github.com/modelcontextprotocol/csharp-sdk/issues/636) is a stage-1 problem
(`NumberHandling` for NaN/Infinity), which is why per-tool options solve it — but our encoder problem
is stage 2, which neither #636 nor #925 touches.

Empirically eliminated (all leave the wire `\uXXXX`-escaped): per-tool `WithTools(options)` (1.1.0
crash / 1.4.0 no-op), SDK upgrade to 1.4.0, `ConfigureJsonOptions` (doesn't exist),
`ConfigureHttpJsonOptions` (wrong pipeline), hand-serializing inside the tool (double-escapes),
reflection (in-place mutate = cached/no-op; replace static = CLR-blocked `initonly`).

**A real fix requires the SDK to serialize transport messages with a configurable
`JsonSerializerOptions`** (so the `Encoder` is settable) — which no issue/PR currently proposes. Until
then, MCP payloads are bounded by pagination + the slim projection only; the UTF-8 win applies solely
to the in-process chat path (`ToolResultJson`). Possible future actions: file an upstream issue/PR to
make the transport message serializer configurable, or fork+patch the SDK's transport layer (high
maintenance — not worth it for the marginal size gain here).

A ready-to-file upstream issue (with repro and the full list of verified dead-ends) is drafted at
[`upstream-issue-mcp-wire-encoder.md`](upstream-issue-mcp-wire-encoder.md) — not yet filed.

## Files to create / modify

| File | Change |
|---|---|
| `OwnPlanner.Infrastructure/Repositories/TaskItemRepository.cs` | DB-side deterministic order + `ListPagedAsync` (+ count) |
| `OwnPlanner.Domain/Tasks/ITaskItemRepository.cs` | Paged method signature |
| `OwnPlanner.Application/.../PagedResult.cs` | New paged envelope record |
| `OwnPlanner.Application/Tasks/ITaskItemService.cs` + impl | Paged overloads, limit/offset clamping |
| `OwnPlanner.Application/Tasks/TaskItemListDto.cs` | New slim list shape (no `createdAt`/`updatedAt`, preview description) |
| `OwnPlanner.Mcp.Tools/TaskItemTools.cs` | `limit`/`offset` params, envelope, slim projection + description preview, updated descriptions |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ToolResultJson.cs` | `UnsafeRelaxedJsonEscaping` encoder (surface #1 — the only place it reaches the wire) |
| `*.csproj` (×5) | `ModelContextProtocol*` → 1.4.0; `Microsoft.Extensions.Logging.Abstractions` → 10.0.7 |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs`, `OwnPlanner.Mcp.StdioApp/Program.cs` | Plain `.WithTools<T>()` (no custom serializer — SDK transport can't be reconfigured); explanatory comment |

## Tests (xUnit v3 + FluentAssertions + NSubstitute, per layer)

- **Repository/Service:** deterministic order incl. `FocusAt` NULLS-LAST and `Id` tiebreaker; paging
  returns correct slice + `TotalCount`/`HasMore`; `limit` clamps to `[1,100]`, `offset` floors at 0;
  `onlyImportant`/`includeCompleted` still apply with paging.
- **Tool layer:** envelope shape; description truncated to preview + suffix in list, full in
  `taskitem_get`; `hasMore` true when `totalCount > offset+limit`.
- **Serialization:** Cyrillic round-trips as UTF-8 (no `\uXXXX`) through `ToolResultJson.Options`
  (surface #1); audit timestamps still dropped.

## Verification (end-to-end, local)

Done by driving the real **stdio MCP host** (same tools/serializer as the `/mcp` endpoint) with a
JSON-RPC `initialize` + `tools/call taskitem_list_items` over stdin, against a DB seeded with 30
Cyrillic tasks (mixed focus dates + null backlog). Confirmed: single 25-item page with
`{totalCount:30, offset, limit:25, hasMore:true}`; deterministic order (focus date asc, NULLS-last);
offset paging with no skips/duplicates; no `createdAt`/`updatedAt`; description preview truncated;
`taskitem_get` still returns full text. **Known limitation:** on the MCP wire Cyrillic remains
`\uXXXX`-escaped (SDK serializer, not reconfigurable), so a Cyrillic-heavy 25-item page is ~37k chars
— bounded by pagination but above 10k; the UTF-8 win applies only to the in-process chat path.
