# OwnPlanner

An AI-powered personal planning assistant that helps you manage tasks, notes, and stay organized with intelligent conversation.

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