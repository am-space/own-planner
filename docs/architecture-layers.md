# Architecture Layers

OwnPlanner follows a strict layer-based clean architecture pattern. Dependencies only flow inwards towards the Domain layer.

## 1. Domain (`OwnPlanner.Domain`)
The core of the application. It contains all the business logic, entities, value objects, and domain rules.
*   **Entities**: Represent core concepts like Tasks, Notes, Goals, and Contexts. Inherit from `EntityBase`.
*   **Rules**: Domain-specific validation and state transitions.
*   **Isolation**: This layer has absolutely no external dependencies (no database, no UI, no frameworks other than standard .NET).

## 2. Application (`OwnPlanner.Application`)
Orchestrates the use cases of the system.
*   **Services / Use Cases**: Coordinates interactions between Domain entities and external resources (abstracted as interfaces).
*   **DTOs**: Data Transfer Objects used to communicate with the Presentation layer.
*   **Organization**: Feature-driven folders (e.g., `Chat/`, `Tasks/`, `Notes/`, `Goals/`).

## 3. Infrastructure (`OwnPlanner.Infrastructure`)
Contains concrete implementations for the interfaces defined in the Application/Domain layers.
*   **Persistence**: EF Core implementations for SQLite context, Repositories, and Database Migrations.
*   **Adapters**: External integrations for APIs, AI (Gemini), and filesystem operations.
*   **Isolation**: Only layer that knows about `Microsoft.EntityFrameworkCore` and external SDKs.

## 4. Presentation (Multiple Entry Points)
These projects references the Application and Infrastructure layers, wiring them together using Dependency Injection.
*   **`OwnPlanner.Web.Server`**: ASP.NET Core project serving the API and React SPA. Handles cookie authentication and API routing.
*   **`OwnPlanner.Mcp.StdioApp`**: Model Context Protocol (MCP) host that executes application logic exposed as AI tools over stdio.
*   **`OwnPlanner.Console`**: A CLI tool for running chat loops via the terminal.

## Cross-Cutting Concerns
*   **Tests**: Test projects mirror the layer or shared component they test (`Domain.Tests`, `Application.Tests`, `Infrastructure.Tests`, `Web.Server.Tests`, `Mcp.Tools.Tests`), ensuring boundaries are respected at test time.
