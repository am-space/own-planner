# Planner workspace

OwnPlanner's authenticated web application keeps chat as its central interaction surface while also
providing deterministic browsing for tasks, goals, and notes plus a recoverable task Trash.

## Application layout

The protected React routes share one `PlannerShell`:

- a responsive left navigation area links Chat, Tasks, Trash, Goals, Notes, and Settings;
- Chat uses the full central workspace;
- Tasks, Goals, and Notes use the upper workspace with the same mounted chat session in a horizontal
  assistant panel below;
- the assistant can be collapsed for more vertical planner space;
- selecting an item opens complete read-only details in a full-height right-side inspector, making
  navigation, planner/chat, and inspector three sibling desktop columns;
- narrow screens show planner data and chat as mutually exclusive surfaces and use drawers for
  navigation and item details.

The navigation collapse control sits in the navigation header: the expanded panel shows a collapse
chevron beside the product name, while the icon rail shows a centered expand chevron. Task and note
rows use `Context · List` as consistent secondary metadata and reserve right-side adornments for
state. Goal rows use `Horizon · Target` because goals have no context or list relationship.

`ChatPage` remains mounted while protected routes change. Its visible messages, draft, planning mode,
quota, context, loading, and error state therefore survive navigation during the current browser
session. Planner section, filters, search, offset, and selected item are represented by the route and
query string. The assistant's collapsed state is a local `sessionStorage` preference.

The Trash route lists recoverable tasks with restore and permanent-delete actions. Permanent deletion
uses an explicit confirmation dialog and cannot be undone.

## HTTP planner API

The additive API surface is authenticated with the existing cookie scheme:

```text
GET /api/planner/tasks
GET /api/planner/tasks/{id}
GET /api/planner/tasks/trash
POST /api/planner/tasks/trash/{id}/restore
DELETE /api/planner/tasks/trash/{id}
GET /api/planner/goals
GET /api/planner/goals/{id}
GET /api/planner/notes
GET /api/planner/notes/{id}
GET /api/planner/filter-options
```

Collections return `items`, `totalCount`, `offset`, `limit`, and `hasMore`. The default page size is
25 and the maximum is 100. Task descriptions, goal descriptions, and note content are bounded to
240-character previews in collection responses; complete content is available only from the matching
detail endpoint.

Task filters include search, status, important-only, task list, context, and linked goal. Goal filters
include search, status, and horizon. Note filters include search, pinned-only, note list, context, and
linked goal. Filtering, deterministic ordering, counting, projection, and paging execute in SQLite
before results are materialized.

The normal response statuses are:

- `200` for a valid collection, detail, or filter-options response;
- `400` for invalid enum, identifier, offset, or limit values;
- `401` when the request is unauthenticated;
- `404` when an item is absent from the authenticated user's database.
- `409` when restore cannot resolve the original task list or permanent deletion targets a task that
  is not in Trash.

## Architecture and tenant isolation

Application owns `IPlannerReadService`, query records, paging validation, response DTOs, and the
`IPlannerReadStore` persistence boundary. Infrastructure implements the boundary with
`PlannerReadStore` and obtains `AppDbContext` exclusively through `IPlannerDbContextFactory`.

`PlannerController` initializes the authenticated user's database through
`IPerUserAppInitializationService` before reading it. The database identity is derived from the
authenticated planner session; planner routes and query parameters never accept a user ID or database
path. The React application calls these HTTP endpoints directly and does not invoke MCP tools for UI
browsing.

Task browsing remains read-only. Trash mutations delegate to the shared `ITaskItemService`, while
tenant selection and initialization remain controller concerns. No route accepts user or database
identity from the client.

## Verification ownership

- Application tests cover validation and normalized query delegation.
- Infrastructure SQLite tests cover filters, paging, preview bounds, relationships, details, and
  inactive/archived filter metadata.
- Web Server tests cover controller mapping, initialization order, and not-found behavior.
- Playwright E2E tests cover desktop and mobile layouts, chat preservation, deep links, paging,
  details, authentication, malformed input, and two-user isolation across every read surface.
