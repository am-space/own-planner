# ADR-0017: Supported runtime and dependency baselines

**Date:** 2026-08-20
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

OwnPlanner's frontend build still targeted end-of-life Node.js 20 and several direct dependencies
were one or more supported majors behind. Major migrations must preserve UI behavior, MCP contracts,
tenant resolution, and existing SQLite files. Some npm and NuGet `latest` versions are not a valid
baseline: direct consumers impose narrower peer or integration constraints.

## Decision

### Runtime and frontend

- Use Node.js 24 LTS consistently through `.node-version`, GitHub Actions, documentation, and the
  Docker build.
- Use Vite 8 with `@vitejs/plugin-react` 6, ESLint 10, `@eslint/js` 10, `globals` 17, and
  `eslint-plugin-react-refresh` 0.5.
- Use Material UI and icons 9. Components use the v9 `slotProps` and `sx` APIs in place of removed
  deprecated props and removed shorthand system props.
- Retain TypeScript 5.9 and `@types/node` 24. The current `typescript-eslint` 8 line supports
  TypeScript only below 6.1, so TypeScript 7 is excluded until that peer constraint is lifted.
- Remove the frontend's historical transitive security overrides. They are no longer required by
  `npm audit`, and the forced `brace-expansion` v2 override is incompatible with ESLint 10.

### MCP and SQLite

- Keep all direct `ModelContextProtocol`, `.Core`, and `.AspNetCore` references together on 2.2.0.
- Configure the authenticated `/mcp` Streamable HTTP endpoint with `Stateless = true` explicitly.
  OwnPlanner exposes request-scoped tools and does not use subscriptions, unsolicited messages, or
  server-to-client requests. Tool names, schemas, results, bearer authentication, and tenant-bound
  database resolution remain unchanged. The stdio host continues to expose the same shared tools.
- Use `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5. EF Core remains responsible for the database schema;
  this native-provider update requires no migration and existing database files remain SQLite 3
  files.

### OpenAPI compatibility gate

- Keep `Microsoft.OpenApi` on the newest compatible 2.x release, 2.12.0. Do not adopt v3 while
  `Microsoft.AspNetCore.OpenApi` 10.x integrates with the v2 API surface. Revisit the pin when the
  ASP.NET Core integration officially supports OpenAPI.NET v3; upgrading OwnPlanner to .NET 11
  solely to unlock it is outside this decision.

## Consequences

### Positive

- Local, CI, and container frontend builds share a supported Node LTS major.
- Direct frontend, MCP, and SQLite dependencies are on current compatible major lines without
  weakening lint rules or changing external planner contracts.
- MCP HTTP state behavior cannot change silently with a future SDK default.

### Negative / Trade-offs

- TypeScript 7, OpenAPI.NET 3, and xUnit 4 remain visible in outdated reports as approved
  compatibility exclusions. xUnit 4 requires migration from the repository's solution-wide VSTest
  invocation to Microsoft Testing Platform; that test-runner and CI workflow change is deferred.
- Material UI 9 changes some generated DOM and CSS internals; browser E2E coverage is the regression
  gate for supported application flows.
- Stateless MCP HTTP cannot provide subscriptions or unsolicited server-to-client requests. Those
  capabilities are not used by OwnPlanner and would require an explicit transport decision later.

## Alternatives Considered

- **Adopt every registry `latest` version** — rejected because TypeScript 7 violates the current
  `typescript-eslint` peer range and OpenAPI.NET 3 is outside the supported ASP.NET Core 10
  integration line.
- **Retain stateful MCP HTTP behavior** — rejected because the server has no session-scoped MCP
  capability and the SDK recommends stateless mode for request-scoped tools.

## Related Files

| File | Role |
|---|---|
| `.node-version` | Local and CI Node.js baseline |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Dockerfile` | Container Node.js baseline |
| `OwnPlanner.Web/ownplanner.web.client/package.json` | Frontend dependency baselines |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` | Explicit stateless MCP HTTP configuration |
| `OwnPlanner.Web/OwnPlanner.Web.Server/OwnPlanner.Web.Server.csproj` | MCP and OpenAPI package versions |
| `OwnPlanner.Infrastructure/OwnPlanner.Infrastructure.csproj` | MCP Core and SQLite native bundle versions |
