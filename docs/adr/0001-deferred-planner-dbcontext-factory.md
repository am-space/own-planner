# ADR-0001: Deferred Per-User AppDbContext Creation via IPlannerDbContextFactory

**Date:** 2026-05-30  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

`AppDbContext` is a per-user SQLite database context. The database file path is derived from the authenticated user's ID (`ownplanner-user-{userId}.db`). The user ID must be resolved either from an active `IPlannerSessionContextAccessor` scope (used by MCP tool flows) or from the authenticated HTTP user's claims (used by web API flows).

The original implementation registered `AppDbContext` as a scoped DI service using a factory delegate:

```csharp
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var userId = sessionContextAccessor.Current?.UserId
        ?? ResolveAuthenticatedUserId(httpContextAccessor.HttpContext); // throws if not authenticated
    var dbPath = Path.Combine(userDbDirectory, $"ownplanner-user-{userId}.db");
    options.UseSqlite($"Data Source={dbPath}");
});
```

`ResolveAuthenticatedUserId` throws `UnauthorizedAccessException` if the HTTP context has no authenticated user. Because `AppDbContext` is **scoped**, this factory runs at the point when any scoped service that transitively depends on `AppDbContext` is first resolved — not only when data is first accessed.

### Problem

Any future unauthenticated scope that resolves a service graph containing `AppDbContext` would throw at DI activation time, not at a controller/authorization boundary. Examples:

- Health-check endpoints
- ASP.NET Core middleware that resolves scoped services
- Background-/hosted-service scopes
- Integration tests that build a full service graph without an HTTP context

All current controllers that touch `AppDbContext` are `[Authorize]`-protected today, which made this safe in practice. However, the safety guarantee was fragile and implicit — nothing in the DI registration signaled the constraint.

---

## Decision

Remove `AddDbContext<AppDbContext>(...)` from the web server DI container. Instead, introduce a deferred factory abstraction `IPlannerDbContextFactory` that creates `AppDbContext` instances only at the point of actual data access.

### Key design elements

1. **`IPlannerDbContextFactory` (Infrastructure layer)**  
   A single-method interface:
   ```csharp
   ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default);
   ```
   Callers are responsible for disposing the returned context.

2. **`PlannerAppDbContextFactory` (Web server, scoped)**  
   Implements the interface by resolving the user ID from `IPlannerSessionContextAccessor.Current` or the authenticated HTTP user claims. Throws `UnauthorizedAccessException` only when `CreateAsync` is called — that is, only when planner data is actually needed.

3. **`PlannerRepositoryBase<TEntity>` (Infrastructure layer)**  
   Replaces `RepositoryBase<TEntity, AppDbContext>` for all planner repositories. Each method opens its own `AppDbContext` via the factory and disposes it before returning:
   ```csharp
   await using var db = await CreateDbContextAsync(ct);
   ```

4. **`FixedPathPlannerDbContextFactory` (MCP stdio host)**  
   A simple fixed-path implementation used in the MCP stdio host, where the user ID is known at startup and the DB path is resolved once.

5. **Per-user initialization** (`PerUserAppInitializationService`)  
   Already operates within an explicit `IPlannerSessionContextAccessor` scope. Updated to obtain `AppDbContext` via the factory rather than from DI directly.

---

## Consequences

### Positive

- **DI resolution is always safe** — resolving any planner repository or service no longer throws for unauthenticated scopes.
- **Failure is localized** — `UnauthorizedAccessException` is thrown at the point of actual data access, making call stacks easier to attribute.
- **Future-proof** — health checks, middleware, background scopes, and anonymous endpoints can be added without accidentally triggering auth-dependent DB construction.
- **Testability** — tests construct repositories with a simple `IPlannerDbContextFactory` stub instead of wiring an authenticated `HttpContext`.

### Negative / Trade-offs

- **No shared `DbContext` across repository calls within a scope** — each repository method opens and closes its own context. EF change tracking does not span multiple method calls.
- **No implicit unit of work** — there is no ambient `DbContext` instance that can collect changes across several repositories before a single `SaveChangesAsync`. If a future feature requires a multi-step atomic write spanning multiple repositories, an explicit unit-of-work or transaction wrapper must be introduced.
- **Slightly more `DbContext` lifecycle overhead** — each repository operation creates a new SQLite connection. Acceptable for this workload (single-user SQLite, low concurrency), but worth noting.

---

## Alternatives Considered

### A) Keep `AddDbContext<AppDbContext>` but guard unauthenticated scopes at middleware level

Place a guard in ASP.NET Core middleware to prevent `AppDbContext` from being resolved in unauthenticated request scopes. Rejected because:
- Does not help non-HTTP scopes (background jobs, tests).
- Pushes a DB-layer concern into the HTTP pipeline.

### B) Use `IDbContextFactory<AppDbContext>` (EF Core built-in)

EF Core's built-in `IDbContextFactory<T>` follows the same deferred pattern. Rejected because:
- It still requires registering `AppDbContext` options (including the user ID resolution) up front via `AddDbContextFactory<AppDbContext>(...)`, so the original DI-time auth dependency remains.
- `IPlannerDbContextFactory` is a narrower abstraction that also expresses user-context semantics more clearly.

### C) Validate user ID in repositories/services, not in the factory

Keep `AppDbContext` scoped as before, but have repositories check for a valid user before executing queries. Rejected because:
- The root cause (DI-time throw) would remain.
- Validation would need to be replicated across every repository method.

---

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Infrastructure/Persistence/IPlannerDbContextFactory.cs` | New abstraction |
| `OwnPlanner.Infrastructure/Repositories/PlannerRepositoryBase.cs` | New planner-specific repository base |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/PlannerAppDbContextFactory.cs` | Web server implementation |
| `OwnPlanner.Mcp.StdioApp/FixedPathPlannerDbContextFactory.cs` | Stdio host implementation |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/PerUserAppInitializationService.cs` | Updated to use factory |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Program.cs` | Registration change |
| `OwnPlanner.Mcp.StdioApp/Program.cs` | Registration addition |
| `OwnPlanner.Web.Server.Tests/Services/PlannerAppDbContextFactoryTests.cs` | Regression tests for this change |

