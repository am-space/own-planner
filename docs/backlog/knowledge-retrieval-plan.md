# OwnPlanner — Knowledge Retrieval and Organization

## Status

**Backlog — Part 2 of 3.** This proposal depends on the accepted Knowledge Core implementation.

When this part ships, update or add the corresponding ADR and archive this plan with the standard
banner.

## Objective

Make stored knowledge useful to the planning assistant through exact-text retrieval, typed relations,
link validation, and simple review scheduling.

## Prerequisite

[`knowledge-core-plan.md`](knowledge-core-plan.md) must pass its acceptance gate. This plan must use
the shipped core contracts rather than duplicating document or revision behavior.

## Scope

- SQLite FTS5 search over canonical document data.
- Ranked, paged results with bounded snippets and structured filters.
- Typed relations from documents to other documents and existing planning entities.
- Link and relation integrity reports.
- One-off review dates and review-due queries.
- Explicit access through relevant chat modes and all MCP transports.

## Deferred

- Embeddings and semantic retrieval.
- Automatic relation suggestions or automatic relation creation from links.
- Review recurrence.
- Claim extraction, contradiction detection, and bitemporal facts.
- Person/CRM entities.
- Saved views and user-defined schemas.
- Proactive mentor notifications.
- Task creation from documents.

## Full-text search

Implement SQLite FTS5 as derived data. Search title, Markdown body, tags, canonical path, and selected
text values extracted from metadata.

```text
SearchAsync(
  query,
  kinds?,
  lifecycles?,
  states?,
  statuses?,
  tags?,
  reviewDueBefore?,
  offset,
  limit)
```

Requirements:

- Apply structured filters before paging.
- Order by FTS rank, then `UpdatedAt DESC`, then `Id ASC` for equal scores.
- Return a bounded snippet and matched-field information.
- Exclude trashed documents by default.
- Provide an idempotent rebuild operation from canonical tables.
- Update canonical data and its FTS projection in the same transaction.
- Verify English, Cyrillic, and mixed-language behavior during an implementation spike.

If FTS5 is unavailable in any supported runtime, stop this part and choose a supported exact-text
fallback before changing the public search contract.

## Relations

```text
KnowledgeRelation
  Id: Guid
  FromDocumentId: Guid
  ToType: KnowledgeTargetType
  ToId: Guid
  RelationType: KnowledgeRelationType
  Description: string?
  SourceRevisionId: Guid?
  CreatedAt: DateTime
```

Part 2 target types:

```text
KnowledgeDocument
Goal
TaskItem
PlanningContext
NoteItem
```

Initial relation types:

```text
references
derived-from
supersedes
contradicts
supports
implements
related-to
```

Relation creation validates that both source and target exist in the current user's database. Creating
the same `(from, toType, toId, relationType)` relation twice is idempotent. Self-relations are rejected
except `related-to` only if a real use case is documented; the default is to reject all self-relations.

Polymorphic targets cannot all use database foreign keys. If an existing planning entity is later
deleted, the relation remains as detectable provenance and `knowledge_validate_links` reports the
missing target. Permanent Knowledge deletion remains deferred.

Application operations:

```text
CreateRelationAsync(request)
ListRelationsAsync(documentId, direction, filters, offset, limit)
DeleteRelationAsync(id)
ValidateLinksAsync(documentIds?, includeArchived)
```

Relation updates are intentionally omitted. Delete and recreate a relation when its type changes;
this keeps the first contract small and its idempotency rules clear.

## Link validation

Validation is read-only and reports:

- broken relative Markdown links;
- unresolved or ambiguous wiki-style links;
- relations whose targets no longer exist;
- duplicate slugs found in metadata;
- documents with no relations.

Parsing links does not rewrite document bodies and does not automatically create relations. Relation
suggestions belong to later work.

## Review workflow

Part 2 supports a single optional `ReviewAt` value already versioned by the core model. Recurrence is
deferred.

```text
SetReviewAsync(id, expectedRevision, reviewAt?)
ListReviewDueAsync(before?, kinds?, includeOverdue, offset, limit)
MarkReviewedAsync(id, expectedRevision, nextReviewAt?, reviewSummary?)
```

Marking a document reviewed creates a complete document revision even when the body is unchanged.
Review queries order overdue documents first, then `ReviewAt ASC`, then `Id ASC`.

## MCP tools

Expose six retrieval tools. Read and write operations remain separate when chat modes need different
permissions:

```text
knowledge_search

knowledge_relation_list

knowledge_relation_manage(
  action = Create | Delete,
  ...
)

knowledge_validate_links

knowledge_review_due

knowledge_review_manage(
  action = Set | MarkReviewed,
  ...
)
```

Register the tools explicitly in the web HTTP MCP host, stdio MCP host, `DirectToolMcpAdapter`, and
selected `ModeConfig` allow-lists. Together with the six core tools, the dedicated Knowledge mode has
12 Knowledge tools and remains under the target of 15 active tools.

Recommended mode policy:

- Knowledge: all core and retrieval tools; write-enabled.
- Global Planning, Reflection, System Analysis, Week Planning, and Day Work: no Knowledge tools
  initially. Users switch to Knowledge mode for knowledge work.

Some existing modes already exceed the preferred active-tool range. Add `knowledge_search` and
`knowledge_get` to another mode only after dynamic tool selection is implemented or that mode's total
active declarations have been reduced below the agreed budget. Tool availability must be driven by
measured workflows, not by exposing every potentially useful capability everywhere.

Do not preload search results. The model should retrieve them on demand.

## Persistence

Add relation tables and the FTS projection to the per-user `AppDbContext`. Generate the relational
migration through the EF CLI. Establish and document how FTS virtual-table/triggers are applied and
rebuilt without hand-authoring a migration from scratch.

Required relation indexes:

```text
KnowledgeRelation(FromDocumentId, RelationType)
KnowledgeRelation(ToType, ToId, RelationType)
KnowledgeRelation(FromDocumentId, ToType, ToId, RelationType) UNIQUE
```

## Build order

1. Spike FTS5 availability, tokenization, snippets, and migration mechanics.
2. Implement the FTS projection, transactional updates, and rebuild verification.
3. Implement filtered, ranked, paged search.
4. Add relation entities, validation, repositories, and idempotent creation.
5. Add read-only link validation.
6. Add one-off review operations.
7. Expose the six tools and update every registration and bounded mode policy.
8. Measure tool payloads with representative Cyrillic-heavy documents.

## Tests

- Search returns expected English, Cyrillic, and mixed-language matches.
- Structured filters are applied before paging.
- Equal-rank results have stable ordering.
- Snippets and total payloads remain bounded.
- Rebuilding FTS produces the same searchable projection.
- Relations cannot cross user databases.
- Duplicate relation creation is idempotent.
- Missing polymorphic targets appear in validation reports.
- Link validation never mutates Markdown.
- Review operations use optimistic concurrency and create revisions.
- Read-only chat modes cannot call mutation tools.
- The dedicated Knowledge mode stays within the total active-tool budget; declaration counts are
  measured in schema and integration tests.

## Acceptance gate

Part 2 is complete when the assistant can reliably find a known document, explain why it matched,
navigate its typed relations, report broken links, and list documents due for review without loading
the complete corpus into context.

## Related documents

- [`knowledge-core-plan.md`](knowledge-core-plan.md) — Part 1 prerequisite.
- [`knowledge-migration-plan.md`](knowledge-migration-plan.md) — Part 3 import/export and cutover.
- [`ai-integration.md`](../ai-integration.md) — current tool-calling flow.
- [`adr/0005-mcp-wire-non-ascii-escaping.md`](../adr/0005-mcp-wire-non-ascii-escaping.md) — non-ASCII payload constraint.
