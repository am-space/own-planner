# OwnPlanner — Knowledge Core

## Status

**Backlog — Part 1 of 3.** This proposal defines the durable document foundation for Knowledge.
It must ship before retrieval, relations, review workflows, or migration are implemented.

When this part ships, record the implemented design in an ADR and move this file to `docs/archive/`
with the standard archival banner.

## Objective

Store durable Markdown knowledge safely, with complete history, optimistic concurrency, and reversible
removal. This part deliberately proves the storage model before adding search or import complexity.

## Scope

- Create, retrieve, update, and page through knowledge documents.
- Create a complete revision for every document mutation.
- Detect stale writes with `expectedRevision`.
- Restore an earlier revision by creating a new head revision.
- Archive, trash, and restore documents.
- Preserve stable, export-safe canonical paths.
- Expose the core operations through the shared MCP tool layer.

## Deferred

- Full-text and semantic search.
- Relations to documents or planning entities.
- Review recurrence and review-due workflows.
- Markdown link parsing and validation.
- Import and export.
- Permanent deletion.
- Kind-specific metadata schemas and user-managed workflows.
- Web UI beyond what is needed to exercise the API.

## Domain model

### KnowledgeDocument

```text
KnowledgeDocument
  Id: Guid
  Title: string
  BodyMarkdown: string
  Kind: KnowledgeKind
  CustomKind: string?
  Lifecycle: KnowledgeLifecycle
  Status: string?
  CanonicalPath: string?
  Tags: IReadOnlyCollection<string>
  MetadataJson: string
  ReviewAt: DateTime?
  State: KnowledgeDocumentState
  CurrentRevision: int
  CreatedAt: DateTime
  UpdatedAt: DateTime
```

Initial kinds:

```text
Profile
Strategy
Session
Analysis
Project
Reference
Content
Template
FreeNote
Custom
```

Initial lifecycles:

```text
Snapshot
Living
Pipeline
Reference
```

Document states:

```text
Active
Archived
Trashed
```

For Part 1, a `Snapshot` is immutable immediately after creation. Draft/finalize behavior is deferred
until a concrete need justifies the additional state transition. Corrections create another document.

`MetadataJson` is opaque, structurally valid JSON. Unknown fields must be preserved. Part 1 does not
validate kind-specific schemas.

### Complete revision snapshots

Every mutation creates a `KnowledgeRevision` containing a full snapshot of all versioned document
state:

```text
KnowledgeRevision
  Id: Guid
  DocumentId: Guid
  Number: int
  Title: string
  BodyMarkdown: string
  Kind: KnowledgeKind
  CustomKind: string?
  Lifecycle: KnowledgeLifecycle
  Status: string?
  CanonicalPath: string?
  TagsJson: string
  MetadataJson: string
  ReviewAt: DateTime?
  State: KnowledgeDocumentState
  ChangeSummary: string?
  Source: RevisionSource
  CreatedAt: DateTime
```

Revision sources are `Manual`, `Mcp`, `Import`, `Restore`, and `System`. `Import` is reserved for Part
3. Restoring revision N copies its versioned state into a new head revision; it never deletes history.

### Canonical path rules

A canonical path is an optional relative Markdown path used later for deterministic export.

- Normalize separators to `/`.
- Reject absolute paths, empty segments, `.` and `..` segments, and NUL characters.
- Normalize Unicode to NFC.
- Require a `.md` suffix.
- Compare normalized paths case-insensitively to prevent cross-platform collisions.
- Enforce uniqueness per user database when non-null.

The database value is the normalized value. Display-friendly source spelling may be preserved later in
import metadata if needed.

## Application contracts

Create `OwnPlanner.Application/Knowledge/` with:

```text
IKnowledgeDocumentService
IKnowledgeRevisionService
KnowledgeDocumentDto
KnowledgeDocumentListDto
KnowledgeRevisionDto
KnowledgeDocumentRequest models
```

Most important service operations, in implementation order:

```text
CreateAsync(request)
GetAsync(id, revisionNumber?)
UpdateAsync(id, expectedRevision, patch)
ListAsync(filters, offset, limit)
ListRevisionsAsync(documentId, offset, limit)
GetRevisionAsync(documentId, revisionNumber)
RestoreRevisionAsync(documentId, revisionNumber, expectedRevision, changeSummary?)
ArchiveAsync(id, expectedRevision)
UnarchiveAsync(id, expectedRevision)
TrashAsync(id, expectedRevision)
RestoreFromTrashAsync(id, expectedRevision)
```

All mutations use `expectedRevision`, including archive and trash transitions. A stale request returns
a specific conflict result containing the current revision number.

List operations use database-side ordering and the existing `PagedResult<T>` envelope. Default order:
`UpdatedAt DESC, Id ASC`. Default limit is 25 and maximum limit is 100.

Repository interfaces belong in `OwnPlanner.Domain/Knowledge/`; EF implementations belong in
Infrastructure and use the existing per-user planner repository pattern.

## MCP tools

The number of implemented tools is less important than the number sent to the model on one turn.
Target no more than 15 active tools in a mode and never expose every OwnPlanner tool by default.

Expose six core tools:

```text
knowledge_create
knowledge_get
knowledge_update
knowledge_list

knowledge_set_state(
  id,
  expectedRevision,
  state = Active | Archived | Trashed
)

knowledge_history(
  action = List | Get | Restore,
  documentId,
  revisionNumber?,
  expectedRevision?,
  changeSummary?,
  offset?,
  limit?
)
```

`knowledge_set_state` groups reversible state transitions. Setting the current state is an idempotent
no-op after validating `expectedRevision`; it does not create an empty revision.

`knowledge_history` groups operations over one revision resource. `Restore` requires
`expectedRevision`; `List` and `Get` ignore mutation-only parameters. This mixed read/write tool is
available only in the dedicated write-enabled Knowledge mode, not in read-only modes.

`knowledge_get` returns the complete body. List and history-list results contain bounded previews and
direct the caller to the corresponding get action.

Register the tool class explicitly in:

- the web HTTP MCP host;
- the stdio MCP host;
- `DirectToolMcpAdapter`;
- the appropriate `ModeConfig` allow-lists.

Add a dedicated Knowledge mode with these six tools and no automatic document preload. Ordinary
planning modes do not receive core mutation or history tools. Part 2 adds only narrowly selected
read tools to those modes.

## Persistence

Add `KnowledgeDocuments` and `KnowledgeRevisions` to `AppDbContext`. Generate the migration with the EF
CLI and specify `AppDbContext` explicitly.

Required indexes:

```text
KnowledgeDocument(CanonicalPath) UNIQUE WHERE CanonicalPath IS NOT NULL
KnowledgeDocument(State, UpdatedAt, Id)
KnowledgeDocument(Kind, Lifecycle, State)
KnowledgeRevision(DocumentId, Number) UNIQUE
```

Document update and revision insertion must use one database transaction. Concurrency enforcement must
be atomic; checking the revision only in application memory is insufficient.

## Build order

1. Define path normalization and domain state transitions with tests.
2. Add document and complete revision entities.
3. Add repositories and the EF migration.
4. Implement create, get, update, and paged list.
5. Implement revision list/get/restore.
6. Implement archive, trash, and restore transitions.
7. Add the six MCP tools, the dedicated Knowledge mode, and all explicit registrations.
8. Verify payload bounds and tenant isolation.

## Tests

- Creation produces revision 1.
- Every mutable field is captured in each revision.
- A stale `expectedRevision` cannot overwrite a newer revision.
- Revision restore creates a new head and preserves later history.
- Snapshots reject updates and state restoration that would mutate their content.
- Archive, unarchive, trash, and restore are reversible and revisioned.
- Canonical path normalization rejects unsafe paths and cross-platform collisions.
- Lists are deterministically ordered and paged.
- Separate user databases cannot observe each other's documents or revisions.
- Tool schemas, defaults, preview limits, and actionable errors match the contract.
- The Knowledge mode exposes only its bounded capability set, and read-only modes cannot restore
  revisions or change document state.

## Acceptance gate

Part 1 is complete when a caller can create a document, update every mutable field, detect a stale
write, inspect and restore any revision, trash and recover the document, and retrieve identical state
through the in-process, HTTP MCP, and stdio MCP surfaces.

## Related documents

- [`knowledge-retrieval-plan.md`](knowledge-retrieval-plan.md) — Part 2: search, relations, and review.
- [`knowledge-migration-plan.md`](knowledge-migration-plan.md) — Part 3: import, export, and cutover.
- [`architecture-layers.md`](../architecture-layers.md) — dependency direction.
- [`adr/0002-chat-context-management.md`](../adr/0002-chat-context-management.md) — bounded tool output precedent.
