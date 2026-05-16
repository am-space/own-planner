# AI Integration & MCP Workflow

OwnPlanner integrates with the Google Gemini API to provide an intelligent conversational interface capable of invoking local application behaviors through the Model Context Protocol (MCP).

## Core Components

1.  **AI Provider**: Google Gemini via `Mscc.GenerativeAI`.
2.  **Web Server / Console Orchestrator**: The host application that manages the conversation state, context window, and tool definitions.
3.  **MCP StdioApp**: A separate command-line application (`OwnPlanner.Mcp.StdioApp`) designed specifically to expose backend Application use-cases as standardized Model Context Protocol (MCP) tools over standard input/output streams.

## The Chat Workflow (Tool Calling)

When a user submits a prompt, the system executes the following loop:

1.  **Request**: The user's input is received via the React UI or CLI.
2.  **LLM Prompting**: The Backend Web Server wraps the underlying conversation history and attaches a dynamic list of available MCP tool definitions.
3.  **Generation & Tool Request**: Gemini responds. If the LLM determines it needs data or needs to perform an action, it pauses generation and emits a `FunctionCall` (Tool Call) request.
4.  **MCP Invocation**: 
    *   The Web Server intercepts the `FunctionCall`.
    *   It forwards the tool name and arguments to the spawned `OwnPlanner.Mcp.StdioApp` process via `stdio`.
5.  **Execution**: `OwnPlanner.Mcp.StdioApp` executes the logic (e.g., creating a task, reading notes) and interacts with the user's specific SQLite database.
6.  **Tool Response**: The result (JSON or text) is returned to the Web Server via `stdio`.
7.  **Resumption**: The Web Server appends the tool result to the conversation history and calls Gemini again so it can synthesize a final response for the user.
8.  **Final Output**: Gemini produces natural language text based on the tool result, which is streamed or returned back to the UI.

## Adding a New Tool

To add a new skill to the AI:
1. Define the core logic in `OwnPlanner.Application`.
2. Wrap it as a tool definition and handler in `OwnPlanner.Mcp.StdioApp`.
3. The orchestration layer will automatically expose this new tool schema to Gemini on the next chat session.
