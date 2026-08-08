# ADR-0009: Read-only planner workspace alongside persistent chat

**Date:** 2026-08-08
**Status:** Accepted
**Deciders:** OwnPlanner maintainers

---

## Context

Planning data was accessible from the web application only by asking the AI assistant. Users could
not deterministically inspect their complete task, goal, or note collections, apply basic filters, or
open an item's full details. A conventional dashboard or detached browser would weaken the product's
chat-centric interaction model, while rendering MCP tool output directly in React would couple the UI
to an AI-facing transport and its payload constraints.

The originating implementation plan is archived at
[`docs/archive/read-only-planner-workspace-plan.md`](../archive/read-only-planner-workspace-plan.md).

## Decision

### 1. Use a shared shell with persistent chat

The protected React application uses `PlannerShell` for left navigation and workspace composition.
Chat occupies the full center when selected. Task, goal, and note routes render a read-only collection
above the same mounted `ChatPage` in a collapsible horizontal assistant region. On narrow screens,
planner and chat are mutually exclusive surfaces. The route query string owns planner filters,
offset, and selected item. On sufficiently wide screens, the right inspector is a full-height sibling
of the central planner/chat column; at narrower widths it becomes an overlay drawer. The planner
route still owns detail retrieval and renders the inspector through a shell-provided host.

Task and note rows consistently present relationships as `Context · List` secondary metadata. Goal
rows use `Horizon · Target`, since goals have no context or list relationship. Right-side row
adornments are reserved for item state rather than repeating relationships. The navigation
collapse/expand control lives in the navigation header, where the changed width is visually anchored.

Keeping one `ChatPage` instance mounted preserves its existing local conversation and composer state
without adding a client state-management dependency or changing the chat response contract.

### 2. Add an Application-owned read-query boundary

`IPlannerReadService` validates paging and filter values and delegates to `IPlannerReadStore`.
Application owns purpose-built summary/detail DTOs and filter records. `PlannerReadStore` in
Infrastructure uses the current tenant's deferred `AppDbContext` to execute filters, deterministic
ordering, counts, projections, and pagination in SQLite. Collection projections bound personal text
previews; detail queries return full content and display names for related lists, contexts, and goals.

### 3. Expose additive authenticated HTTP endpoints

`PlannerController` exposes `/api/planner` collection, detail, and filter-option reads. It ensures the
current user's database is migrated and seeded, then resolves all data through the existing
authenticated tenant factory. It accepts no tenant selector. React calls this HTTP contract directly;
MCP, Gemini, console, and stdio contracts remain unchanged.

## Consequences

### Positive

- Users can inspect complete, paged planner data without model interpretation while continuing the
  same chat.
- A full-height inspector keeps chat visually between the left navigation and right context panel.
- URL-addressable view state supports refresh, sharing within an authenticated session, and a future
  allowlisted UI-action contract.
- Database-side projection and bounded previews keep collection payloads and server memory bounded.
- The existing per-user database boundary continues to provide tenant isolation without a central
  cross-user planner index.

### Negative / Trade-offs

- The horizontal split creates two independent scroll regions and requires fixed minimum sizes.
- The persistent three-column arrangement is limited to wide screens; the inspector must overlay the
  center at narrower desktop and tablet widths.
- The first MVP uses offset pagination and SQLite substring search; it does not provide saved views,
  fuzzy/full-text search, custom sorting, or real-time synchronization.
- Chat presentation state survives route navigation but is not persisted across a full browser reload.
- The React workspace currently keeps planner query logic in one page module; split it further only if
  additional entity browsers make that module materially harder to maintain.

## Alternatives Considered

- **Replace chat with a planner page** — rejected because simultaneous context and conversation are
  valuable and chat remains the main product surface.
- **Vertical split or detached window** — rejected in favor of a horizontal assistant that gives list
  rows the full central width and preserves an optional right inspector.
- **Dashboard landing page** — rejected because summary cards were not the underlying use case.
- **Call MCP from React** — rejected because HTTP read use cases provide a stable authenticated UI
  contract without coupling browser behavior to AI tool schemas.

## Deferred

Planner mutations, custom assistant resizing, saved views, AI-controlled navigation, full-text search,
and auxiliary right-side controls remain deferred.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Application/Planner/` | Read contracts, DTOs, validation, and persistence boundary |
| `OwnPlanner.Infrastructure/Planner/PlannerReadStore.cs` | Tenant-bound database read projections |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/PlannerController.cs` | Authenticated additive HTTP API |
| `OwnPlanner.Web/ownplanner.web.client/src/components/PlannerShell.tsx` | Persistent navigation and chat workspace composition |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/PlannerPage.tsx` | URL-driven collection, filter, paging, and inspector UI |
| `OwnPlanner.E2E.Tests/PlannerWorkspaceE2eTests.cs` | End-to-end layout, persistence, deep-link, paging, and mobile evidence |
| `OwnPlanner.E2E.Tests/TenantIsolationE2eTests.cs` | Two-user planner API isolation evidence |
