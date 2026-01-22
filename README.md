# OwnPlanner

An AI-powered personal planning assistant that helps you manage tasks, notes, and stay organized with intelligent conversation.

## Demo

Web app: https://app.controlcode.space

## Architecture

The solution is organized as a layered .NET application with multiple entry points (Web, Console, MCP) and an AI chat workflow that can invoke MCP tools.

### System Context (C4 L1)

```mermaid
graph LR
  user["End user"] --> ui["OwnPlanner Web App<br/>React 18 + TypeScript + MUI"]
  ui --> web["OwnPlanner Web Server<br/>ASP.NET Core (.NET 9)<br/>Cookie Auth + API"]

  web --> llm["Google Gemini API<br/>(LLM)"]
  web --> mcp["OwnPlanner MCP Stdio App<br/>(MCP tools server)"]

  dev["Developer / Automation"] --> console["OwnPlanner.Console<br/>CLI chat"]
  console --> llm
  console --> mcp
```

### Containers & Data (C4 L2)

```mermaid
graph TB
  subgraph client["Client"]
    browser["Browser<br/>React SPA"]
  end

  subgraph host["Host / Container"]
    web["OwnPlanner.Web.Server<br/>ASP.NET Core (.NET 9)"]
    mcpapp["OwnPlanner.Mcp.StdioApp<br/>MCP tools over stdio<br/>(spawned process)"]
    authdb["SQLite: ownplanner-auth.db<br/>(users/auth)"]
    userdb["SQLite: ownplanner-user-{userId}.db<br/>(tasks/notes per-user)"]
    logs["File logs<br/>/app/data/logs"]
  end

  llm["Google Gemini API"]

  browser --> web
  web --> authdb
  web --> llm
  web --> mcpapp
  web --> logs
  mcpapp --> userdb
  mcpapp --> logs
```

### Layered Code Structure

```mermaid
graph BT
  presentation["Presentation<br/>OwnPlanner.Web.Server<br/>OwnPlanner.Console<br/>OwnPlanner.Mcp.StdioApp"]
  infrastructure["Infrastructure<br/>OwnPlanner.Infrastructure<br/>EF Core, SQLite, adapters"]
  application["Application<br/>OwnPlanner.Application<br/>use-cases, services, DTOs"]
  domain["Domain<br/>OwnPlanner.Domain<br/>entities, rules"]
  external["External<br/>Gemini API, MCP SDK"]

  presentation --> application
  presentation --> infrastructure
  infrastructure --> application
  application --> domain
  infrastructure --> external
  presentation --> external
```

### Runtime: Chat + MCP tool call flow

```mermaid
sequenceDiagram
  participant B as Browser (React)
  participant W as Web Server (ASP.NET Core)
  participant G as Gemini API
  participant M as MCP Stdio App (process)
  participant D as SQLite (per-user db)

  B->>W: POST /chat (message)
  W->>G: Send prompt + tool definitions
  G-->>W: Tool call request (e.g., tasklist_list_create)
  W->>M: Call tool over stdio (MCP)
  M->>D: Read/write tasks/lists
  D-->>M: OK
  M-->>W: Tool result (text/json)
  W->>G: Provide tool result to continue
  G-->>W: Final assistant message
  W-->>B: Response
```

## Solution Overview

OwnPlanner is a multi-project .NET 9 solution for personal planning and task management. It features a layered architecture and multiple interfaces:

- Core
  - **OwnPlanner.Application**: Core business logic, services, and DTOs.
  - **OwnPlanner.Domain**: Domain models and business rules.
  - **OwnPlanner.Application.Tests**, **OwnPlanner.Domain.Tests** : Unit tests for respective layers.
- Infrastructure
  - **OwnPlanner.Infrastructure**: Data persistence, external integrations, and infrastructure services.
  - **OwnPlanner.Infrastructure.Tests**: Integration tests for infrastructure.
- Presentation
  - **OwnPlanner.Web.Server**: ASP.NET Core 9 web server with React frontend for user interaction.
  - **OwnPlanner.Mcp.StdioApp**: MCP stdio adapter and developer tools for command-line or protocol-based automation.
  - **OwnPlanner.Console**: Console application for direct CLI usage.

Key features include:
- Layered architecture for maintainability and testability
- AI integration for intelligent planning
- Logging, error handling, and developer tooling
- Automated tests for core logic and infrastructure

## Built With

- Frontend: React 18 + TypeScript + Material-UI
- Backend: .NET 9 + ASP.NET Core
- AI: Google Gemini + Mscc.GenerativeAI SDK

## Run with OwnPlanner.Console

### Prerequisites

- .NET SDK 9
- A Google Gemini API key

### Configure

The console app loads settings from `appsettings.json` and `appsettings.Development.json` in the `OwnPlanner.Console` directory.

Required:

- `Gemini:ApiKey` (string)

Optional:

- `Gemini:Model` (string)
- `Gemini:MaxToolCallRounds` (number)

### Run

From the repo root:

```sh
dotnet run --project OwnPlanner.Console
```

Or from the project directory:

```sh
cd OwnPlanner.Console
dotnet run
```

Type `exit` to quit.