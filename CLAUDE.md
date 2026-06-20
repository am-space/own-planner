# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

OwnPlanner is an AI-powered personal planning assistant: a layered .NET 10 solution with a React + TypeScript (Vite, MUI) frontend. Users chat with a Google Gemini LLM that invokes in-process MCP-style tools to manage tasks, notes, goals, and contexts stored in per-user SQLite databases.

## Common commands

Run from the repo root unless noted.

```sh
# Build / test the whole solution
dotnet build OwnPlanner.sln -c Release
dotnet test

# Single test project, class, or method
dotnet test OwnPlanner.Application.Tests/OwnPlanner.Application.Tests.csproj
dotnet test --filter "FullyQualifiedName~OwnPlanner.Domain.Tests.SomeClass"
dotnet test --filter "DisplayName~my_test_name"

# Run the web server (auto-launches the Vite dev server via SpaProxy)
dotnet run --project OwnPlanner.Web/OwnPlanner.Web.Server   # http://localhost:5079

# Run the console chat loop (type `exit` to quit)
dotnet run --project OwnPlanner.Console
```

Frontend (from `OwnPlanner.Web/ownplanner.web.client/`):

```sh
npm ci
npm run dev      # Vite dev server
npm run build    # tsc -b && vite build
npm run lint     # eslint
```

CI (`.github/workflows/ci.yml`) runs, in order: `npm ci` → `npm run lint` → `npm run build` → `dotnet restore` → `dotnet build -c Release` → `dotnet test -c Release`. Match this before pushing.

## Architecture

Strict clean-architecture layering; dependencies only flow inward toward the Domain. Test projects mirror each layer (`*.Tests`).

- **OwnPlanner.Domain** — entities (inherit `EntityBase`: Tasks, Notes, Goals, Contexts), value objects, domain rules. No external dependencies, not even EF Core.
- **OwnPlanner.Application** — use cases, services, DTOs. Feature-driven folders (`Chat/`, `Tasks/`, `Notes/`, `Goals/`). Depends only on Domain via interfaces.
- **OwnPlanner.Infrastructure** — the only layer that references EF Core and external SDKs (Gemini via `Mscc.GenerativeAI`). Holds the two `DbContext`s and all migrations.
- **Presentation** (three entry points that wire Application + Infrastructure via DI): `OwnPlanner.Web.Server` (ASP.NET Core API + React SPA, cookie auth), `OwnPlanner.Console` (CLI chat), `OwnPlanner.Mcp.StdioApp` (MCP stdio host).
- **OwnPlanner.Mcp.Tools** — tool definitions/handlers shared by the web server and the stdio host.

Deeper docs live in `docs/`: `architecture-layers.md`, `ai-integration.md`, `database-schema.md`, plus ADRs in `docs/adr/` and superseded planning docs in `docs/archive/`. See [Documentation process](#documentation-process) for how plans and ADRs are managed.

### Per-user database model

Data is split across two SQLite databases (see `docs/database-schema.md`):

- **`ownplanner-auth.db`** (`AuthDbContext`) — single central DB for accounts, credentials, sessions, and the user→database mapping.
- **`ownplanner-user-{userId}.db`** (`AppDbContext`) — one isolated file per user holding all planning entities, mounted on login via a tenant-aware context.

In the web server, per-user context resolution and DB wiring live in `Services/` (`PlannerSessionContextAccessor`, `PlannerAppDbContextFactory`, `PerUserAppInitializationService`). Local debug `.db` files and the `OwnPlanner.Web.Server/data/` directory are gitignored.

### Chat + tool-calling flow

The web server orchestrates the conversation, attaches available tool definitions to each Gemini request, and executes returned `FunctionCall`s **in-process** via `DirectToolMcpAdapter` (resolving tool implementations from DI against the authenticated user's database), then feeds results back to Gemini for the final reply. The console/stdio path uses the same tools over the MCP stdio host instead. See `docs/ai-integration.md`.

**To add a new AI tool:** implement the core logic in `OwnPlanner.Application`, then expose it as a tool definition + handler in `OwnPlanner.Mcp.Tools` so both the web server and stdio host reuse it. The orchestration layer surfaces the new schema to Gemini automatically.

## EF Core migrations

Never hand-write migrations — always use the CLI, and specify the context explicitly (there are two):

```sh
dotnet ef migrations add <Name> --project OwnPlanner.Infrastructure --context AppDbContext
dotnet ef migrations add <Name> --project OwnPlanner.Infrastructure --context AuthDbContext
```

Append `--startup-project <path>` if the startup project can't be inferred.

## Conventions

- Testing stack: xUnit v3 + FluentAssertions + NSubstitute. Keep tests in the test project matching the layer under change.
- Follow existing style, naming, and folder conventions; prefer existing abstractions over new patterns. Keep changes small and scoped.
- Preserve nullability annotations and existing async APIs; keep parameter ordering consistent across similar APIs.
- When adding or changing an interface, add/update XML doc comments describing intent and contracts.
- Keep logging structured. Don't break public contracts unless the task requires it.

## Documentation process

Docs in `docs/` come in three kinds: **plans** (`docs/*-plan.md`, written before building),
**ADRs** (`docs/adr/NNNN-*.md`, durable records of a decision as shipped — start from
`docs/adr/template.md`), and **reference docs** (living "how it works now" pages). When a planned
feature ships, write/update its ADR and `git mv` the plan into `docs/archive/` with a banner linking
to the ADR — never delete plans, and don't let a finished plan pose as current documentation. Full
mechanics (numbering, banner format, superseding) are in [`docs/CLAUDE.md`](docs/CLAUDE.md).

`Directory.Build.props` derives assembly/file versions from the `APP_VERSION` / `APP_FILE_VERSION` env vars (default `0.0.0-local`). Releases are cut from git tags `v<major>.<minor>.<patch>`; tagged CI builds push a Docker image to `ghcr.io`.
