# ADR-0002: Chat Context Management — Reducing Per-Turn Token Usage

**Date:** 2026-06-14  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

Chat runs against `gemini-flash-latest` with a self-imposed context cap of 64k tokens
(`GeminiSettings.MaxContextLengthTokens`). In practice sessions hit that cap — both on mode entry
and, more sharply, as a conversation continued — and the chat failed with
`ChatContextLimitExceededException`.

The root cause was not the cap but how much data the chat path fed into the model. The flow:

1. On mode switch, `PlanningService.LoadContextAsync` calls every tool in `ModeConfig.PreloadTools`
   and dumps each tool's **full JSON result** into the system prompt.
2. Tool results were serialized as raw web-JSON DTOs (full GUIDs, audit timestamps, null fields).
3. `GetResponseAsync` re-ran the preload **every turn** in DayWork mode and prepended it to the
   user message.
4. Gemini's `ChatSession` keeps the **entire history** forever — nothing trimmed it.

Six contributing problems were identified, biggest first:

1. **DayWork re-injected full context into history every turn** (`RefreshOnTurn: true`). Because
   history is append-only, N turns stacked N copies of the task list — roughly quadratic growth.
   This was the primary "fills up during use" cause.
2. **Full-fidelity JSON DTOs.** Each row carried 3 GUIDs (~17–20 tokens each), 3–4 ISO timestamps,
   and null fields, even though the system prompts tell the model not to show IDs.
3. **Note `Content` dumped wholesale.** `noteitem_list_items` returned every note's full free-text
   body; Reflection, GlobalPlanning, and SystemAnalysis all preload notes.
4. **Preloads over-fetched.** DayWork preloaded all incomplete tasks on top of today's focus list,
   contradicting its "today only" scope.
5. **No history management.** The only guardrail was throwing at the limit — the chat died instead
   of recovering.
6. **All ~47 tool schemas attached to most modes.** `AllowedTools: []` disabled filtering.

---

## Decision

Adopt a coordinated set of measures across the chat path. Each is scoped to the **web in-process
tool path** (`DirectToolMcpAdapter` + `PlanningService` + `ChatServiceAdapter`), which is what
drives the live chat; the stdio MCP host is a secondary path and is noted where relevant.

### 1. Stop per-turn context re-injection (model pulls on demand)

Removed the `RefreshOnTurn` mechanism entirely (`ModeConfig`, `PlanningService.GetResponseAsync`).
The user message is now forwarded directly. The on-entry preload still seeds the first turn; the
DayWork system prompt instructs the model to call `taskitem_list_by_focus_date` itself when it
needs current state. Nothing accumulates per turn.

### 2a. Compact tool-result serialization (GUIDs retained)

Tool results serialize through a dedicated `ToolResultJson.Options`:
- **Null fields omitted** (`DefaultIgnoreCondition = WhenWritingNull`).
- **Audit timestamps `CreatedAt`/`UpdatedAt` stripped** via a `JsonTypeInfo` modifier (the model
  never uses them; ordering is server-side). Functional dates `dueAt`/`focusAt` are kept, in ISO 8601.

Argument binding/schema generation still use plain web options, so only what the model *reads* is
trimmed. **GUIDs are intentionally kept**; id shrinking is deferred (see Alternatives / 2b).

### 3. Truncate note content in list views

`noteitem_list_items` and `noteitem_list_by_goal` return a 200-char content preview with a
`… [truncated — call noteitem_get for full content]` hint. Implemented in the MCP tool layer via a
`NoteItemDto with { Content = … }` projection, so the shape is unchanged and the web/UI path (which
calls `INoteItemService` directly) keeps full content.

### 4. Trim per-mode preloads

Dropped `taskitem_list_items` from DayWork (the focus-date list already covers today). Other modes'
list preloads already default to lean filters (active-only, exclude archived/completed). WeekPlanning's
"next-7-days only" and SystemAnalysis's full snapshot were left as-is (the former needs a date-range
tool that doesn't exist yet; the latter is inherent to its diagnostic purpose).

### 5. History compaction instead of throwing (shadow transcript + rebuild)

`PlanningService` keeps a plain-text transcript of completed turns (`ChatMessage`/`ChatRole`). When
the projected next-turn size crosses a **soft threshold (70% of max)**, it compacts and rebuilds the
chat session from `[system prompt] + [compacted history] + [last 3 turns]` via the new
`IChatAdapter.RebuildSession`. Two strategies (`HistoryCompactionStrategy`):
- **Summarize** (default) — `IChatAdapter.SummarizeAsync` condenses the older span on a tool-less
  side session; falls back to **Trim** on failure.
- **Trim** — drops the older span.

The hard `ChatContextLimitExceededException` remains only as a last resort (a single turn larger than
the whole budget). Thresholds, retained-turn count, and strategy are constructor-configurable.

### 6. Per-mode tool allow-lists

Each mode declares an explicit allow-list following one rule: **full CRUD for owned entity types,
read-only tools for referenced types, `datetime_get_current`** everywhere, and `search_agent_call`
for the analytical modes (DayWork omits web search to stay narrow). Tool counts drop from 47 to
~39 (GlobalPlanning), ~22 (WeekPlanning), ~19 (Reflection), ~13 (DayWork). Preload tools bypass the
allow-list but are also allow-listed so the model can refresh them.

---

## Consequences

### Positive

- **No quadratic growth during use** — the #1 driver of mid-session failures is gone.
- **Smaller per-turn payloads** — null/timestamp trimming, truncated note bodies, and leaner
  preloads cut row size ~35–45% without touching GUIDs; per-mode allow-lists cut fixed schema
  overhead.
- **Graceful degradation** — long sessions compact rather than dying; the hard throw is now a true
  last resort.
- **UI unaffected** — all trimming lives in the tool/chat path; `INoteItemService` and the web API
  still return full DTOs.
- **Guarded by tests** — serializer behavior, note truncation, compaction strategies, and per-mode
  allow-list validity all have unit tests (including a typo guard against unknown tool names).

### Negative / Trade-offs

- **GUIDs still dominate row tokens.** 2a deliberately kept them; the larger id-token win awaits 2b.
- **Audit timestamps unavailable to the model**, including in `*_get` calls. Accepted — the model
  never reasoned over them.
- **Compaction loses mid-turn tool-call structure** for older turns (they collapse into the
  assistant's final text). Acceptable for continuity; the model never needs past function-call parts.
- **Summarize adds one model call + latency** when it fires (rare after #1–#4).
- **Web-path only.** Null/timestamp trimming is applied via `ToolResultJson` on the in-process
  adapter; the stdio MCP host serializes via the framework and is not covered. The shared
  tool-layer changes (#3, #4, #6) benefit both paths.
- **Allow-list risk.** A mode can't call a tool outside its list; mitigated by the typo-guard test
  and by erring toward full CRUD on owned entity types.

---

## Alternatives Considered

### #1 — stopping refresh accumulation

- **(a) Strip the previous `[Refreshed context]` block from `ChatSession.History` each turn** —
  rejected as fragile; depends on the Mscc library's internal history representation.
- **(c) Ephemeral-context parameter on the adapter** — viable but more machinery.
- **Chosen (b): model pulls on demand** — removes accumulation by construction.

### #2 — entity ids

- **Handle map (`t1`/`n1` ↔ GUID)** — biggest id-token saving but adds per-session state,
  two-adapter plumbing, and hallucination/stale-handle failure modes.
- **Short-prefix GUIDs (first 8 chars, resolve by prefix)** — middle ground, no persistent map.
- **Chosen: 2a keeps full GUIDs now; 2b (handles or prefixes) deferred** until measured token
  numbers justify the added state/risk.

### #5 — history management

- **Trim-only** — simplest, but loses old context.
- **Mutating the chat SDK's internal history** — rejected (same fragility as #1a; risks splitting
  Gemini function-call/response pairs).
- **Chosen: shadow transcript + rebuild, Summarize with Trim fallback** — avoids library internals
  and keeps function-call integrity by replaying our own transcript through `StartChat`.

---

## Deferred

- **2b — id shrinking** (handle map or short-prefix GUIDs). Revisit with real `PromptTokenCount`
  numbers (logged by `ChatServiceAdapter.LogUsageMetadata`).
- **Stdio path serialization** — apply null/timestamp trimming at the MCP framework serializer level.
- **WeekPlanning date-range preload** — needs a new range query tool/repository method.

---

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Chat/ModeConfig.cs` | Per-mode preloads, allow-lists, prompts (#1, #4, #6) |
| `OwnPlanner.Application/Chat/PlanningService.cs` | Orchestration, history compaction (#1, #5) |
| `OwnPlanner.Application/Chat/ChatMessage.cs` | Transcript entry type (#5) |
| `OwnPlanner.Application/Chat/HistoryCompactionStrategy.cs` | Summarize/Trim strategy (#5) |
| `OwnPlanner.Application/Chat/IChatAdapter.cs` | `RebuildSession` + `SummarizeAsync` contracts (#5) |
| `OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs` | Session rebuild + summarization (#5) |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/ToolResultJson.cs` | Null-omit + drop audit timestamps (#2a) |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/DirectToolMcpAdapter.cs` | Uses `ToolResultJson.Options` (#2a) |
| `OwnPlanner.Mcp.Tools/NoteItemTools.cs` | Note content truncation (#3) |
| `OwnPlanner.Application.Tests/Chat/PlanningServiceTests.cs` | Compaction + limit tests (#1, #5) |
| `OwnPlanner.Application.Tests/Chat/ModeConfigTests.cs` | Allow-list invariants (#6) |
| `OwnPlanner.Web.Server.Tests/Services/ToolResultJsonTests.cs` | Serializer trimming tests (#2a) |
| `OwnPlanner.Web.Server.Tests/Services/DirectToolMcpAdapterTests.cs` | Tool-name typo guard (#6) |
| `OwnPlanner.Mcp.Tools.Tests/NoteItemToolsTests.cs` | Note truncation tests (#3) |
