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

1.  **Request**: The user's input is received via the React UI or CLI.
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
