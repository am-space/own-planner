# AI Integration & MCP Workflow

OwnPlanner integrates with the Google Gemini API to provide an intelligent conversational interface capable of invoking local application behaviors through the Model Context Protocol (MCP).

## Core Components

1.  **AI Provider**: Google Gemini via `Mscc.GenerativeAI`.
2.  **Web Server / Console Orchestrator**: The host application that manages the conversation state, context window, and tool definitions.
3.  **Direct tool adapter**: The web server executes planner MCP-style tools in-process via `DirectToolMcpAdapter`, resolving tool implementations from its own dependency injection container and authenticated user context.
4.  **MCP StdioApp**: A separate command-line application (`OwnPlanner.Mcp.StdioApp`) kept for stdio-based hosts such as the console tooling.
5.  **MCP HTTP endpoint**: The web server also exposes `/mcp` over Streamable HTTP for external MCP clients, authenticated with a dedicated bearer scheme.

The web host constructs its Gemini adapter through `IChatAdapterFactory`. Production resolves
`GeminiChatAdapterFactory`; deterministic browser tests replace only this composition boundary while
leaving planning, MCP execution, tenant resolution, and persistence real. See
[`testing.md`](testing.md) and [ADR-0008](adr/0008-deterministic-browser-e2e-testing.md).

## The Chat Workflow (Tool Calling)

When a user submits a prompt, the system executes the following loop:

1.  **Request**: The user's input is received via the React UI, private Telegram bot, or CLI.
2.  **LLM Prompting**: The Backend Web Server wraps the underlying conversation history and attaches a dynamic list of available MCP tool definitions.
3.  **Generation & Tool Request**: Gemini responds. If the LLM determines it needs data or needs to perform an action, it pauses generation and emits a `FunctionCall` (Tool Call) request.
4.  **Tool Invocation**:
    *   The Web Server intercepts the `FunctionCall`.
    *   It resolves the matching tool implementation from DI and executes it directly for the authenticated user.
5.  **Execution**: The tool logic interacts with the user's specific SQLite database through the web server's per-user database wiring.
6.  **Tool Response**: The result (JSON or text) is returned to the chat orchestration layer in-process.
7.  **Resumption**: The Web Server appends the tool result to the conversation history and calls Gemini again so it can synthesize a final response for the user.
8.  **Final Output**: Gemini produces natural language text based on the tool result, which is streamed or returned back to the UI.

## Adding a New Tool

To add a new skill to the AI:
1. Define the core logic in `OwnPlanner.Application`.
2. Wrap it as a tool definition and handler in `OwnPlanner.Mcp.Tools` so the web server and stdio host can both reuse it.
3. The orchestration layer will automatically expose this new tool schema to Gemini on the next chat session.

## Task Trash tools

`taskitem_delete` is a backward-compatible recoverable operation: it moves an active task to Trash
and does not remove its stored task data. Trashed tasks are absent from normal task queries and all
planning reports. `taskitem_list_trash` returns the authenticated user's paged Trash contents, and
`taskitem_restore` restores a task only when its original task list still exists.

Permanent deletion is intentionally not an MCP tool. It is available only through the authenticated
web Trash flow after explicit user confirmation, and the Application service rejects permanent
deletion unless the task is already in Trash.

## Delegated Agents

Global Planning exposes the local `task_planning_agent_call` capability. It accepts a required
planning objective and optional `contextId` and `taskListId` scopes. This capability is registered
by the Gemini chat adapter rather than as a public MCP tool: the main planner decides what outcome
to delegate, while the specialist performs the bounded task decomposition in a fresh Gemini
session that receives no parent conversation history.

The Task Planning Agent uses the host's existing `IMcpAdapter`, so web execution continues through
the authenticated `DirectToolMcpAdapter` and its user-bound `AppDbContext`; console and stdio-backed
chat use their configured adapter. A provider-neutral `TaskPlanningMcpAdapter` wraps that adapter
for each invocation. It exposes a server-owned allowlist, rejects recursive agent calls and
reopen/restore/archive/permanent-delete operations, validates supplied scope IDs, and checks every
task or task-list read and write against the active scope. The allowlist permits completing an active
task and moving one to recoverable Trash. Both lifecycle mutations first resolve the target through
the authenticated adapter and prove it belongs to any context or task-list scope. The trusted
specialist instruction permits them only when the delegated objective explicitly expresses that
intent and identifies the target sufficiently; ambiguous targets are returned as unresolved
questions. Broad task queries that cannot be proven in scope are unavailable during scoped delegation.

Each invocation has a configurable tool-call-round limit (eight by default) and propagates cancellation through scope
validation and tool execution. Its structured result distinguishes status, a factual summary,
attempted mutations, warnings, and unresolved questions. Nested Gemini usage metadata contributes
to the parent turn's token totals when the provider supplies it. Failures are returned as safe
delegation results and do not reset the main conversation.

This differs from `search_agent_call`, which starts a separate tool-free Gemini session with Google
Search enabled and returns current factual information. Neither specialist can call itself or the
other specialist.

## Strategic Report Preloading

Global Planning and System Analysis preload `strategic_report_get` rather than retrieving broad
goal, context, list, task, and note collections. The additive, read-only tool is also available for
an explicit refresh; existing entity tools remain available for targeted drill-down and mutations
allowed by the active mode.

The report captures one `asOfUtc` instant and returns:

- overall totals for non-archived contexts, active goals, non-archived task and note lists,
  incomplete tasks, important incomplete tasks, overdue incomplete tasks, and notes;
- a summary for every non-archived context and active goal;
- deterministic structural signals for goals and contexts without active tasks, tasks without a
  goal, and contexts without task or note lists; and
- compact task and note samples containing exact identifiers for subsequent tool calls.

An overdue task is incomplete and has `dueAt < asOfUtc`. Important counts include only incomplete
tasks. Tasks in archived task lists, notes in archived note lists, and completed tasks do not
contribute to active metrics. Task samples sort overdue first, then important, then by the nearest
due/focus instant and a stable identifier. Note samples sort pinned first, then most recently
updated and a stable identifier.

`taskSampleLimit` defaults to 3 and `noteSampleLimit` defaults to 2; both accept values from 0 through
5. Text previews contain at most 200 characters and carry a separate truncation flag. Counts remain
complete when a sample limit is zero. The report does not perform LLM analysis, infer note types, or
replace detail tools.

`IStrategicReportReader` defines the Application contract. `StrategicReportReader` executes the
cross-entity projections through the host-provided `IPlannerDbContextFactory`, so web calls use the
authenticated user's database and stdio calls use that host's configured database. The tool accepts
no user ID, database path, or other tenant selector.

## Weekly Report Preloading

Week Planning preloads `weekly_report_get` instead of broad goal, task-list, and task snapshots. The
existing entity tools remain available for targeted drill-down and permitted task changes. Calling
the report again refreshes the point-in-time workload after a conversation changes planner data.

The optional `startDate` is a strict ISO UTC calendar date (`yyyy-MM-dd`). When omitted, the reader
uses the calendar date of its injected `asOfUtc` instant. The report covers exactly seven UTC dates
using `[windowStartDate, windowEndExclusiveDate)` and returns both endpoints, `asOfUtc`, `timeZone:
"UTC"`, and the interval notation. It does not apply a local week start or daylight-saving offset.

Only incomplete tasks in non-archived task lists contribute. Overall totals separately count tasks
focused inside the window, due inside the window, currently overdue (`dueAt < asOfUtc`), currently
important, and currently lacking both focus and due dates. A task focused and due inside the window
appears in both relevant views but contributes once to distinct window/day task totals. Focus dates
are flexible work plans; due dates are fixed commitments and are never inferred from focus dates.

Each day contains separate focus and due counts and bounded samples. Context summaries include an
explicit `Unassigned or missing context` bucket for legacy associations; active-goal summaries make
coverage available for drill-down. Signals identify overloaded days, active goals without focused
work, overdue tasks not focused inside the window, and important tasks without a focus date.

`taskSampleLimit` defaults to 3 and accepts 0–5. `overloadedDayThreshold` defaults to 5 and accepts
1–20; a day is overloaded when its distinct focus-or-due task count is at least the threshold. Task
samples sort overdue first, then important, due date, focus date, and stable identifier. Description
previews contain at most 200 characters and include a truncation flag. The tool performs no capacity
estimation, scheduling, timezone conversion, or historical reconstruction, and accepts no tenant
selector.

## Reflection Report Preloading

Reflection mode preloads `reflection_report_get` instead of broad goal, note-list, note, and task
snapshots. Existing entity tools remain available for targeted drill-down and permitted Reflection
mode writes. Calling the report again refreshes its current-state view after relevant changes.

`periodDays` defaults to 7 and accepts 1–31. The optional `endAtUtc` must be an ISO timestamp with a
zero UTC offset, such as `2026-08-19T12:00:00Z`; otherwise the injected current UTC instant is used.
The report returns `asOfUtc`, `periodStartUtc`, `periodEndExclusiveUtc`, `timeZone: "UTC"`, and the
explicit interval `[periodStartUtc, periodEndExclusiveUtc)`.

The report deterministically returns:

- tasks currently completed whose persisted `completedAt` is inside the period;
- tasks created inside the period and notes created or updated inside the period;
- currently incomplete tasks whose current focus instant is inside the period;
- currently overdue incomplete tasks as of `asOfUtc`;
- completed and missed-focus summaries by non-archived context and active goal;
- active-goal completion coverage plus current remaining and overdue task counts; and
- notes currently stored in the non-archived well-known Inbox note list.

Signals identify focused-but-incomplete tasks from the period, active goals without currently
recorded completions in the period, current overdue carryover, and current Inbox accumulation.
Missing, legacy, or archived context associations use an explicit unresolved bucket instead of
dropping tasks. Archived task and note lists do not contribute to active/current metrics.

Task and note sample limits each default to 3 and accept 0–5. Completed samples order by most recent
completion, then importance, due date, focus date, and stable identifier. Unresolved samples order
overdue first, then important, due date, focus date, and identifier. Inbox samples order pinned
first, then most recent update and identifier. Description and content previews contain at most 200
characters and carry separate truncation flags; zero limits retain complete counts without content.

This is deliberately a current-state report, not an audit log. A reopened task has no current
`CompletedAt` and cannot be reported as a prior completion. Prior goal statuses, task/context/goal
assignments, and notes removed from Inbox cannot be reconstructed. These limitations are included in
the tool result so the model does not present unavailable history as fact. The tool performs no note
type inference, sentiment analysis, scoring, comparison, or retrospective-note generation and
accepts no tenant or timezone selector.

## Telegram presentation channel

Telegram is an optional presentation adapter, not an MCP tool or a second planning implementation.
After a user explicitly links a private Telegram identity, ordinary bot text uses a distinct
`telegram:<telegram-user-id>` session and the existing `IChatSessionManager`, `PlanningService`,
usage quota service, Gemini adapter, and tenant-bound `DirectToolMcpAdapter`. Web and Telegram
conversation histories are separate while planner entities remain shared.

The mapping from Telegram numeric user and private-chat IDs to the OwnPlanner user is resolved only
from `AuthDbContext`; usernames and request-supplied OwnPlanner identifiers are never authorization
inputs. The saved mode is restored after in-memory session expiry. See
[`telegram-integration.md`](telegram-integration.md) for linking, commands, deduplication, failure,
and deployment behavior.
