# Chat Context Optimization Proposal

## Problem

The chat context limit is capped at 64k tokens (`GeminiSettings.MaxContextLengthTokens`,
default `64 * 1024`). In practice this fills up too quickly — both on mode entry and,
more sharply, as a conversation continues. Sessions hit
`ChatContextLimitExceededException` and die instead of degrading gracefully.

The root cause is not the cap itself but how much data the chat path feeds into the
model: full-fidelity JSON DTOs (GUIDs + timestamps), unbounded note bodies, broad
preloads, and — worst of all — per-turn context re-injection that accumulates in
history.

## How context gets built (data flow)

1. `PlanningService.SwitchModeAsync` → `LoadContextAsync` calls every tool in
   `ModeConfig.PreloadTools` and dumps each tool's **full JSON result** into the system
   prompt (`OwnPlanner.Application/Chat/PlanningService.cs:101`).
2. Tool results are serialized as raw web-JSON DTOs
   (`OwnPlanner.Web/OwnPlanner.Web.Server/Services/DirectToolMcpAdapter.cs:77`).
3. `GetResponseAsync` re-runs the preload **every turn** for DayWork mode and prepends it
   to the user message (`OwnPlanner.Application/Chat/PlanningService.cs:53`).
4. Gemini's `ChatSession` keeps the **entire history** forever — nothing trims it
   (`OwnPlanner.Infrastructure/Adapters/ChatServiceAdapter.cs:340`).

## Root causes, biggest first

### 1. DayWork re-injects full context into history every turn ⭐ main culprit — ✅ DONE

`RefreshOnTurn: true` prepended `[Refreshed context]\n{full task dump}` to each user
message (`PlanningService.cs:53-58`). Because `ChatSession` history is append-only, after
N turns there were **N stacked copies** of the task list in history — roughly quadratic
growth. This was the primary reason context fills up *during* use.

**Fixed via option (b) — model pulls on demand.** Removed the `RefreshOnTurn` mechanism
entirely: dropped the field from `ModeConfig`, deleted the per-turn refresh branch in
`PlanningService.GetResponseAsync` (it now forwards the user message directly), and updated
the DayWork system prompt to tell the model to call `taskitem_list_by_focus_date` itself
when it needs current state. The on-entry preload still seeds the first turn; nothing
accumulates per turn anymore. (Other options considered: (a) strip the previous
`[Refreshed context]` block from history each turn — rejected as fragile, depends on the
Mscc library's internal history shape; (c) an ephemeral-context parameter on the adapter.)

### 2. Full-fidelity JSON DTOs — GUIDs and timestamps dominate

Each `TaskItemDto` serializes 13 fields including **4 GUIDs** (`Id`, `TaskListId`,
`GoalId`, ...) and **4 ISO timestamps** (`CreatedAt`, `UpdatedAt`, `DueAt`,
`CompletedAt`). GUIDs are ~36 chars and tokenize poorly. Every system prompt already says
*"don't show entity IDs unless asked"* — yet we pay full token cost to feed them in.

**Fix:** build compact LLM-facing projections instead of dumping DTOs:
- Render terse lines/markdown, not JSON:
  `- [ ] Buy milk · due 6/14 · #context` beats
  `{"id":"...","title":"Buy milk","createdAt":"..."}`.
- Drop `CreatedAt`/`UpdatedAt`/`CompletedAt` from list views.
- Replace 36-char GUIDs with short per-turn handles (e.g. `t1`, `t2`) mapped back to GUIDs
  server-side when a write tool is called. Biggest single saver; keeps write-by-id working.

### 3. Note `Content` is unbounded and dumped wholesale

`noteitem_list_items` returns every note **including the full `Content`** body
(`OwnPlanner.Mcp.Tools/NoteItemTools.cs:40`). Retrospective/Brief notes are free text and
can be large. Reflection, GlobalPlanning, and SystemAnalysis all preload this.

**Fix:** in list/preload views return title + truncated snippet (first ~200 chars); full
body only via `noteitem_get` when the model asks.

### 4. Preloads fetch more than the mode needs

- **DayWork** preloads `taskitem_list_by_focus_date` **and** `taskitem_list_items` (ALL
  incomplete tasks) — the second contradicts "today only" (`ModeConfig.cs:93`). Drop it.
- **WeekPlanning** loads all tasks but only needs next-7-days + overdue.
- **SystemAnalysis** preloads the **entire database** (goals + contexts + all lists + all
  tasks + all notes with content) in one shot (`ModeConfig.cs:153`). Worst-case single
  payload.

**Fix:** add filters/limits to preload calls (exclude completed, date-bound, cap counts,
titles-only for notes).

### 5. No history management — it only throws

The sole guardrail is `EnsureContextWithinLimit` throwing
`ChatContextLimitExceededException` (`PlanningService.cs:71`). At 64k the chat dies instead
of recovering.

**Fix:** add a sliding-window trim or summarize-old-turns step before sending, so long
sessions degrade gracefully.

### 6. (Minor) All ~30 tool schemas attached to most modes

`AllowedTools: []` disables filtering, so every non-SystemAnalysis mode exposes the full
tool set's schemas (`ChatServiceAdapter.cs:207`). Smaller effect (schemas count once, not
per turn), but per-mode allow-lists recover a few thousand tokens of fixed overhead.

## Recommended order of work

| Priority | Change | Effort | Impact |
|---|---|---|---|
| 1 | ✅ Stop DayWork from stacking refreshed context in history (done — model pulls on demand) | Low | Huge — fixes growth-during-use |
| 2 | Compact projections (drop timestamps, short ids, lines not JSON) | Med | Huge — ~3-5× smaller preloads |
| 3 | Truncate note `Content` in list views | Low | Large for note-heavy modes |
| 4 | Filter/limit preloads per mode (drop `taskitem_list_items` from DayWork) | Low | Large |
| 5 | History trim/summarize instead of throwing | Med | Resilience |
| 6 | Per-mode tool allow-lists | Low | Small, fixed savings |

## Notes

- The model is `gemini-flash-latest`, whose real context window is far larger than 64k —
  the 64k is a self-imposed cap. Two independent levers: shrink what we feed (above) and/or
  raise the cap. Shrinking is worth doing regardless, since #1 and #2 waste tokens and
  money even with a bigger window.
- Items #1 and #2 are the highest-impact pair and a good starting point.
</content>
</invoke>
