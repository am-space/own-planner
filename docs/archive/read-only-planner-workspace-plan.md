# Read-only planner workspace plan

> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0009: Read-only planner workspace alongside persistent chat](../adr/0009-read-only-planner-workspace.md).
> Details below may not reflect later refinements made during review.

**Status:** Implemented
**Date:** 2026-08-07

## Intended outcome

Add an authenticated, read-only planner workspace that lets users browse and filter all of their
tasks, goals, and notes without asking the AI assistant, while preserving chat as the central
interaction surface through a horizontal assistant panel below each planner view.

## Canonical issue

GitHub issue [#29: Add a read-only planner workspace](https://github.com/am-space/own-planner/issues/29)
is the canonical product contract for this feature. This plan records the implementation design,
impact, verification strategy, and reviewed assumptions that support that issue.

## Problem statement

OwnPlanner currently exposes planning data primarily through natural-language chat and MCP tool
calls. Users cannot directly inspect the complete set of tasks, goals, or notes, apply predictable
filters, or open an item's details through the React application. This makes the assistant the only
way to discover data that already belongs to the user.

The feature must add conventional browsing without turning OwnPlanner into a dashboard-first
application. Chat remains the primary product surface and stays available from every planner view.

## Product principles

1. **Chat remains central.** Selecting Chat shows the full chat experience. Selecting planning data
   shows that data with the same chat session available in a horizontal panel below it.
2. **Browsing is deterministic.** Users can reach complete, paginated collections and apply explicit
   filters without relying on model interpretation.
3. **The MVP is read-only.** It does not add create, update, complete, pin, archive, or delete actions.
4. **One source of planning behavior.** The UI uses authenticated Application read use cases through
   HTTP; it does not invoke MCP tools or duplicate business rules in React.
5. **Tenant isolation is non-negotiable.** Every query resolves the database from the authenticated
   planner session. No request can select a user or database path.
6. **The layout remains extensible.** A contextual inspector may occupy the right side without
   changing the left navigation, central workspace, or bottom assistant model.

## User stories

- As an authenticated user, I can open Tasks, Goals, or Notes from the primary navigation and inspect
  my data directly.
- As a user with many items, I can page through the complete matching collection and understand how
  many items match the current filters.
- As a user, I can search and apply entity-appropriate filters with predictable results.
- As a user, I can open the full read-only details of an item without leaving the planner workspace.
- As a user, I can keep chatting while viewing planning data and can collapse the assistant when I
  need more vertical space.
- As a user, I can switch between Chat and planner sections without losing the visible chat history
  or active server-side chat session.
- As a mobile user, I can access the same data without unusable simultaneous panes.

## Functional requirements

### FR-1: Authenticated application shell

The protected React application shall use a shared application shell with:

- a left primary navigation area;
- a central workspace;
- a horizontal assistant region below the central planner workspace when applicable;
- an optional contextual inspector region on the right.

The navigation shall contain Chat, Tasks, Goals, Notes, and Settings. It shall be persistent on
supported desktop widths, collapsible to an icon rail, and available as a temporary drawer on narrow
screens.

### FR-2: Chat section

Selecting Chat shall display the existing chat experience across the full central workspace. No
planner list shall remain above it.

Navigation away from and back to Chat shall preserve:

- the visible message history for the current authenticated session;
- the selected planning mode;
- unsent message draft text;
- context, quota, loading, and error state that is still current.

The implementation shall either keep the chat feature mounted within the application shell or move
its presentation state into a shell-owned React context. It shall not rely on route unmounting that
clears the existing `ChatPage` component state.

### FR-3: Planner sections and horizontal assistant

Selecting Tasks, Goals, or Notes shall show:

1. the selected read-only planner view in the upper central workspace; and
2. the current chat session in a horizontal panel below it.

The horizontal assistant shall support:

- **open**, showing recent conversation content and the composer;
- **collapsed**, showing a compact `Ask OwnPlanner...` affordance and unread/loading/error indication
  when relevant.

The MVP may use fixed open and collapsed sizes. Drag-to-resize and persisted custom height are
deferred. The default desktop state when entering a planner section is open. A user collapse action
shall be retained while navigating among Tasks, Goals, and Notes during the current browser session.

The planner workspace and assistant shall have independent scroll regions. Keyboard focus shall not
jump between them during background loading or pagination.

### FR-4: Contextual inspector

Selecting a task, goal, or note shall open a read-only contextual inspector on the right. The
inspector shall:

- show the complete item detail available to the user;
- show human-readable related list, context, and goal names where applicable;
- be independently closable without clearing collection filters or pagination;
- span the application content height on sufficiently wide screens;
- become an overlay drawer or dedicated detail view on narrow screens;
- never expose another user's item, even when an item identifier is manually changed.

The inspector is the MVP use of the right-side extension point. Editing controls, advanced filter
editors, and other auxiliary tools are deferred.

### FR-5: Task browser

The Tasks section shall expose every task owned by the authenticated user across pages. Its default
view shall show open tasks. Users shall be able to include completed tasks and reach the full
collection.

The MVP task filters are:

- case-insensitive text search over title and description;
- status: Open, Completed, or All;
- important only;
- task list;
- planning context, resolved through the task list;
- linked goal.

Task results shall use the existing deterministic order: focus date ascending with unscheduled tasks
last, then most recently updated, then identifier as the total-order tiebreaker. User-selectable sort,
focus-date presets, and due-date presets are deferred.

Task list rows shall return a bounded description preview rather than the complete description. The
inspector shall load the complete task detail separately.

### FR-6: Goal browser

The Goals section shall expose every goal owned by the authenticated user across pages. Its default
view shall show active goals. Users shall be able to include achieved and dropped goals and reach the
full collection.

The MVP goal filters are:

- case-insensitive text search over title and description;
- status: Active, Achieved, Dropped, or All;
- horizon: Monthly, Quarterly, Yearly, or TargetDate.

Goal results shall default to most recently updated first, then identifier as a deterministic
tiebreaker. Rows shall contain summary data; the inspector shall load the complete goal detail.

### FR-7: Note browser

The Notes section shall expose every note owned by the authenticated user across pages.

The MVP note filters are:

- case-insensitive text search over title and content;
- pinned only;
- note list;
- planning context, resolved through the note list;
- linked goal.

Notes shall default to pinned first, then most recently updated, then identifier as a deterministic
tiebreaker. List rows shall return a bounded content preview. Complete note content shall be loaded
only for the selected inspector item.

### FR-8: Paging and collection state

All three collections shall use server-side filtering, ordering, counting, and pagination. Loading a
page must not first materialize the user's complete collection in Application or Web Server memory.

Collection responses shall include:

- `items`;
- `totalCount`;
- `offset`;
- `limit`;
- `hasMore`.

The default page size shall be 25 and the maximum accepted page size shall be 100, matching the
existing task paging convention. The UI shall expose deterministic pagination controls and preserve
the selected item only while it remains available under the current filters.

Each view shall provide distinct loading, empty, no-filter-results, error, and unauthorized states.

### FR-9: URL-addressable view state

The active planner section, filters, search text, page offset, and selected item shall be representable
in the route and query string. Examples:

```text
/planner/tasks?status=open&important=true&offset=0
/planner/goals?status=active&horizon=quarterly
/planner/notes?pinned=true&noteListId=<id>&selected=<id>
```

Refreshing or following a valid URL shall restore the same read-only view after authentication.
Invalid enum values, malformed identifiers, negative offsets, and unsupported limits shall receive a
stable validation response and shall not select a different tenant or database.

The assistant panel's open/collapsed presentation is local UI preference and does not need to be
encoded in shared URLs.

### FR-10: Responsive behavior

At narrow widths, the UI shall not attempt to show navigation, planner collection, horizontal chat,
and inspector simultaneously.

- Left navigation becomes a temporary drawer.
- Planner and Chat become mutually exclusive full-screen views.
- Selecting an item opens a full-width detail drawer or detail view.
- Returning from detail restores the collection filters and scroll/page state.

The exact breakpoint shall follow the existing MUI theme rather than introduce a second breakpoint
system.

### FR-11: Accessibility

Navigation, filters, pagination, assistant expand/collapse, collection rows, and inspector close shall
be operable by keyboard and expose accessible names. The active navigation section, selected filter,
selected item, expanded state, loading state, and errors shall not be conveyed by color alone.

Focus shall move to the inspector heading when it opens and return to the originating collection row
when it closes where that row remains rendered.

### FR-12: AI compatibility without AI-controlled UI in the MVP

The MVP shall not let model output directly navigate or filter the React UI. It shall make collection
state serializable and externally settable so a later additive chat response contract can request a
typed action such as `showPlannerView`.

A future UI-action contract must allowlist section names, filters, and identifiers. It must not accept
an arbitrary client route, user identifier, database path, or executable content from the model.

## HTTP read contract

Add authenticated, additive HTTP read endpoints under a planner-specific route. The proposed surface
is:

```text
GET /api/planner/tasks
GET /api/planner/tasks/{id}
GET /api/planner/goals
GET /api/planner/goals/{id}
GET /api/planner/notes
GET /api/planner/notes/{id}
GET /api/planner/filter-options
```

Collection query parameters shall map to the filters in FR-5 through FR-8. `filter-options` shall
return the current user's task lists, note lists, planning contexts, and goals required to render
human-readable choices. It shall include referenced archived/inactive metadata when necessary to
display an existing item's relationships without presenting unavailable options as active choices.

List endpoints shall return purpose-built summary DTOs with bounded previews. Detail endpoints shall
return explicit read DTOs with complete user content and related display names. No endpoint shall
accept `userId`, database path, or tenant selector from the client.

Expected status semantics:

- `200 OK` for valid collection, detail, and filter-option responses;
- `400 Bad Request` for malformed filters or paging parameters;
- `401 Unauthorized` for unauthenticated access;
- `404 Not Found` when the requested item does not exist in the authenticated user's database.

Search text and returned personal content shall not be written to routine application logs. Errors
shall not reveal whether an identifier exists in another user's database.

## Application and persistence design

Define read-query use cases, filter records, summary/detail DTOs, and dependency interfaces in
Application. Implement database-side filtering, deterministic ordering, projection, counting, and
pagination in Infrastructure against the tenant-bound `AppDbContext`.

This read path may reuse existing entity services where their contracts already fit, but it shall not
force HTTP concerns into Domain entities or MCP handlers. The recommended shape is an
Application-owned planner read-query abstraction implemented by Infrastructure, because the UI needs
cross-entity display names and read-optimized projections that do not belong in Domain invariants.

The Web API controller remains a thin authenticated adapter over the Application use cases. The React
client calls the HTTP endpoints directly through the existing API service pattern.

## Acceptance criteria and evidence

| ID | Acceptance criterion | Planned change | Required evidence |
| --- | --- | --- | --- |
| AC-1 | An authenticated user can navigate among Chat, Tasks, Goals, Notes, and Settings | Add the protected application shell and responsive navigation | Browser E2E covers desktop navigation and protected-route behavior |
| AC-2 | Chat is full-screen in Chat and appears below every planner collection on desktop | Split the current page into reusable shell, workspace, and ChatPanel components | Browser E2E verifies full-chat and horizontal-split states |
| AC-3 | Navigation does not clear visible chat history or the active chat session | Keep ChatPanel mounted or lift its state to a shell-owned context | Browser E2E sends a message, visits each planner section, returns, and finds the message |
| AC-4 | The assistant can be collapsed and reopened without changing collection state | Add open/collapsed assistant presentation state | Browser E2E verifies filters, page, and selection remain stable across collapse/open |
| AC-5 | Users can reach every matching task, including completed tasks | Add paged task query, status/filter controls, and total metadata | Application/Infrastructure tests cover filters, ordering, and multiple pages; browser E2E reaches a later page |
| AC-6 | Users can reach every matching goal, including achieved and dropped goals | Add paged goal query and filters | Application/Infrastructure tests cover status/horizon/search and paging |
| AC-7 | Users can reach every matching note and filter it predictably | Add paged note query and filters | Application/Infrastructure tests cover pinned/list/context/goal/search and paging |
| AC-8 | Collection payloads remain bounded and complete content loads only on selection | Add summary DTOs with previews and separate detail endpoints | Contract tests assert preview bounds and detail completeness |
| AC-9 | Selecting an item opens only that user's full read-only details | Add contextual inspector and ownership-scoped detail endpoints | Web Server tests cover `200`, same-tenant `404`, unauthenticated `401`, and two-user isolation for each entity |
| AC-10 | Valid planner URLs restore section, filters, page, and selection | Store view state in React Router paths/query parameters | Browser E2E loads representative deep links and verifies restored controls/results |
| AC-11 | Empty, filtered-empty, loading, error, and narrow-screen states remain usable | Add explicit MUI states and responsive behavior | Browser E2E plus manual accessibility/responsive review at desktop and mobile widths |
| AC-12 | The MVP does not change MCP, Gemini, console, or stored-data contracts | Keep the new vertical slice on Application read queries, HTTP, and React | Diff/contract review and existing MCP/console verification remain green |
| AC-13 | One user can never observe another user's planning data or filter metadata | Resolve all reads through the authenticated tenant-aware context | Two-user Web Server and browser E2E coverage for collections, details, and filter options |

## Impact map

| Area | Impact |
| --- | --- |
| Domain | Not affected: no entity, invariant, or Domain behavior changes are required |
| Application | Affected: read queries, filter contracts, paged summary/detail DTOs, and dependency interfaces |
| Infrastructure | Affected: EF Core read projections, filters, related-name joins, deterministic ordering, count, and pagination |
| Web API/server | Affected: authenticated planner read endpoints, DI registration, validation, and tenant-isolation tests |
| MCP tools | Not affected: existing AI-facing tools and schemas remain unchanged |
| Console/stdio | Not affected: no presentation or MCP exposure change |
| React frontend | Affected: shared application shell, navigation, reusable ChatPanel state, planner views, filters, pagination, and inspector |
| Tests | Affected: Application/Infrastructure query tests, Web Server contract/isolation tests, and browser E2E flows |
| Documentation | Affected: this active plan and index; shipped architecture/reference docs and ADR before archival |

## Compatibility and migrations

- No EF Core migration or stored-data change is expected for the MVP.
- Existing HTTP, authentication, chat, MCP, console, and stdio contracts remain compatible.
- New HTTP routes and DTOs are additive external contracts.
- Existing `TaskItemDto`, `GoalDto`, and `NoteItemDto` do not become UI list contracts; purpose-built
  summary DTOs prevent unbounded description/content payloads.
- The feature uses the existing per-user `AppDbContext` path. It must not add a central cross-user
  planner index.
- Case-insensitive substring search uses the existing SQLite store. Full-text search, fuzzy matching,
  and FTS migrations are deferred.
- If implementation profiling demonstrates that an index is required, the plan must be revised and
  the migration generated with the EF CLI for `AppDbContext`; it must not be handwritten.

## Implementation order

1. Refactor the protected React surface into an application shell and reusable ChatPanel without
   changing current chat behavior. Prove visible history survives section navigation.
2. Add Application read-query contracts and focused tests for task, goal, note, and filter-option
   behavior.
3. Implement the Infrastructure query path with database-side projection, filtering, ordering,
   counting, and pagination. Add focused persistence tests, including archived/inactive relationship
   display edge cases.
4. Add thin authenticated planner endpoints, validation, DI wiring, and Web Server tests for
   unauthenticated access, not-found behavior, and two-user isolation.
5. Add typed React API contracts and Tasks, Goals, and Notes views with URL state, explicit collection
   states, pagination, and basic filters.
6. Add the horizontal assistant states and right-side read-only inspector, then complete responsive
   and accessibility behavior.
7. Add browser E2E scenarios for navigation, chat preservation, representative filters/deep links,
   paging, detail inspection, responsive behavior, and tenant isolation.
8. Review the completed slice against every acceptance criterion, update living reference docs,
   record the shipped layout/read-path decision in an ADR, and archive this plan according to
   `docs/AGENTS.md`.

## Plan review

Reviewed on 2026-08-07 before implementation:

- Every functional requirement maps to acceptance evidence.
- The design solves the underlying direct-browsing use case rather than presenting MCP output in a
  different container.
- Chat remains the central application surface and its current session is preserved across planner
  navigation.
- Application owns the read use cases and contracts; Infrastructure owns EF queries; controllers and
  React remain presentation adapters.
- Tenant identity continues to come exclusively from the authenticated principal and established
  planner context factory.
- List payloads are bounded, and full note/task content is returned only from authenticated detail
  reads.
- The HTTP surface is additive. MCP, Gemini, database, console, and stdio contracts remain unchanged.
- No migration is expected, and any discovered need for one requires a reviewed plan update and an
  EF-generated migration.
- Search terms and personal planning content are not added to routine logs.
- The MVP excludes mutation, dashboard, detached-window, freeform resize, saved-view, and AI-driven
  UI behavior.

The feature contract is implementation-ready. The precise visual styling, MUI component composition,
and responsive breakpoint values may follow the existing theme as long as they preserve these
requirements.

## Verification

Focused checks while implementing:

```sh
dotnet test OwnPlanner.Application.Tests/OwnPlanner.Application.Tests.csproj
dotnet test OwnPlanner.Infrastructure.Tests/OwnPlanner.Infrastructure.Tests.csproj
dotnet test OwnPlanner.Web.Server.Tests/OwnPlanner.Web.Server.Tests.csproj
dotnet test OwnPlanner.E2E.Tests/OwnPlanner.E2E.Tests.csproj --filter "Category=E2E"
./scripts/verify.sh --frontend
```

Final gate:

```sh
./scripts/verify.sh --all
git diff --check
git status --short
```

The final review shall map every acceptance criterion to a named passing automated test or an explicit
manual accessibility/responsive check. A skipped or sandbox-blocked check remains incomplete.

## Exclusions

- Creating, editing, completing, reopening, pinning, archiving, moving, or deleting planning data.
- A dashboard or summary-card home page.
- Opening planning data in a separate browser window.
- A dedicated Contexts workspace; contexts are filter and relationship metadata in this MVP.
- Drag-to-resize or saved custom sizes for the horizontal assistant.
- User-selectable result sorting, saved filters, saved views, and bulk selection.
- Focus-date and due-date preset filters.
- Full-text indexing, fuzzy search, semantic search, or cross-entity global search.
- AI-generated UI navigation or filters, including changes to `ChatResponse` for UI actions.
- New or changed MCP tools, console commands, or stdio behavior.
- Real-time multi-tab synchronization, push updates, and collaborative editing.
- Auxiliary right-side controls beyond read-only item details.

## Risks and assumptions

- The current Chat page owns substantial local state and assumes viewport height. Extracting a
  reusable ChatPanel must preserve current planning-mode, quota, error, focus, and auto-scroll
  behavior.
- Horizontal splitting creates two scroll regions. The UI must keep the composer reachable and avoid
  leaving either region unusably short, especially on laptop-height displays.
- Searching note content and task descriptions with SQLite substring matching is acceptable for the
  expected MVP dataset size. Query profiling should precede any indexing change.
- Joining list, context, and goal display names must not turn paging into client-side materialization
  or N+1 queries.
- Archived lists and inactive goals may remain referenced by current items. Summary/detail DTOs must
  display those relationships without silently dropping the item.
- The right inspector is closed by default and opens only after an explicit item selection. It is an
  independent extension region, not a prerequisite for collection loading.
- Desktop planner sections default to an open assistant panel; the user may collapse it. Mobile uses
  mutually exclusive full-screen planner and chat views.
