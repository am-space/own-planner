# OwnPlanner — Knowledge Migration and Portability

## Status

**Backlog — Part 3 of 3.** This proposal depends on the accepted Knowledge Core and Knowledge Retrieval
implementations.

When this part ships, record the final migration and portability design in an ADR and archive this
plan with the standard banner.

## Objective

Import the existing Markdown corpus without loss, prove deterministic Markdown export, and make an
explicit decision about moving the authoritative master from Markdown/git to OwnPlanner.

## Prerequisites

- [`knowledge-core-plan.md`](knowledge-core-plan.md) passes its acceptance gate.
- [`knowledge-retrieval-plan.md`](knowledge-retrieval-plan.md) passes its acceptance gate.
- A sanitized fixture represents every lifecycle and metadata shape in the source corpus.

## Scope

- Stage a Markdown ZIP bundle outside MCP tool arguments.
- Analyze an import without modifying canonical data.
- Execute an approved import atomically.
- Make repeated imports idempotent through normalized paths and checksums.
- Export one document or the complete corpus as deterministic Markdown.
- Include complete revision and relation data in a machine-readable manifest.
- Validate round trips before changing the authoritative master.

## Deferred

- Resumable background import jobs.
- Incremental export and global change cursors.
- Bidirectional synchronization.
- Automatic conflict merging.
- Import from remote providers.
- Rich web editing and bulk workflow UI.
- Semantic indexes in the portability format; they remain rebuildable derived data.

## Staged bundle contract

Large files do not travel through MCP parameters. Add an authenticated application endpoint that
accepts a ZIP and returns a short-lived opaque `bundleId`:

```text
POST /api/knowledge/import-bundles
  -> { bundleId, expiresAt, fileCount, totalBytes }
```

The staging service:

- enforces configured compressed and uncompressed size limits;
- rejects path traversal, absolute paths, symlinks, and duplicate normalized paths;
- stores artifacts under a unique temporary directory;
- computes checksums while streaming;
- removes expired or consumed bundles;
- never loads the complete corpus into memory.

The same staged `bundleId` contract is used by the web UI and MCP tools. Local stdio callers may use
a small host command or API client to stage a ZIP, but `knowledge_import_analyze` receives only the
opaque ID.

## Import analysis

```text
AnalyzeImportAsync(bundleId, conflictPolicy)

knowledge_import_analyze(
  bundleId,
  conflictPolicy=Fail,
  parseMarkdownLinks=true,
  parseWikiLinks=true)
```

Initial conflict policies:

```text
Fail
Skip
UpdateByCanonicalPath
CreateNew
```

`UpdateIfSourceNewer` is omitted because filesystem timestamps are not a reliable ordering contract.

Analysis returns:

- documents to create, update, skip, or reject;
- inferred kind and lifecycle with warnings where inference is uncertain;
- normalized paths and checksums;
- frontmatter parse errors and preserved unknown fields;
- path, slug, and checksum conflicts;
- broken and ambiguous links;
- proposed typed relations;
- a complete source-path mapping preview;
- a short-lived `analysisId` and confirmation token.

Analysis persists the exact source checksums and intended actions. Execute rejects the plan if the
staged bundle, target documents, or relevant head revisions changed after analysis.

## Atomic import execution

```text
ExecuteImportAsync(analysisId, confirmationToken)

knowledge_import_execute(analysisId, confirmationToken)
```

The first implementation is atomic. All documents, revisions, and relations are committed in one
per-user SQLite transaction or none are committed. If corpus measurements show this is not viable,
stop and write a separate resumable-job plan rather than weakening this contract in place.

Requirements:

- Use normal Knowledge services and domain rules rather than bypassing them.
- Create revision 1 for new documents and an `Import` revision for updates.
- Never duplicate documents or relations when the same bundle is rerun.
- Modify existing documents only according to the analyzed conflict policy.
- Preserve source Markdown bodies without destructive link rewriting.
- Return the complete `sourcePath -> documentId` mapping and error report.
- Keep Markdown/git authoritative until cutover is explicitly accepted.

## Deterministic export

```text
ExportDocumentAsync(id)
ExportAllAsync(includeArchived=true, includeHistory=true)

knowledge_export(
  scope = Document | All,
  documentId?,
  includeArchived=true,
  includeHistory=true
)
```

There is no `sinceRevision` parameter until the core model has a global monotonic change cursor.
`documentId` is required for `Document` scope and rejected for `All` scope.

The canonical bundle contains:

- Markdown files at normalized canonical paths;
- normalized frontmatter with stable field and collection ordering;
- a versioned JSON manifest containing documents, revisions, relations, and checksums;
- a conflict report when a document cannot receive a canonical path;
- no FTS or semantic-index data.

Determinism rules:

- UTF-8 without BOM and LF line endings.
- Unicode normalized to NFC.
- Stable ordinal ordering for files, fields, tags, revisions, and relations.
- Normalized ZIP entry timestamps and attributes.
- No generated export timestamp inside the canonical bundle.
- Identical database state produces byte-identical Markdown and manifest files.

Operational timestamps may be returned in the HTTP/MCP response envelope, but they are not written to
canonical export artifacts.

Reuse the existing account-export temporary artifact pattern where appropriate. Knowledge Markdown
export supplements, but does not replace, the complete SQLite GDPR account export recorded in
[ADR-0006](../adr/0006-gdpr-account-export.md).

## Round-trip validation

Use a sanitized fixture containing:

- living profile and strategy documents;
- an immutable session snapshot;
- an analysis with a review date and multiple revisions;
- technical and project references;
- content pipeline status and custom metadata;
- Markdown and wiki links, including broken and ambiguous cases;
- relations to planning entities;
- English, Cyrillic, and mixed-language content.

Validate:

```text
source bundle -> analyze -> import -> export A
export A -> analyze -> import into clean user DB -> export B
compare canonical contents of export A and export B
```

Any intentional non-round-tripping field must be documented in the manifest schema and approved
before cutover.

## MCP and application integration

Expose three migration tools through the shared tool project:

```text
knowledge_import_analyze
knowledge_import_execute
knowledge_export
```

Register them explicitly in the HTTP MCP host, stdio MCP host, and `DirectToolMcpAdapter`.

Import execution is a write operation available only in an appropriate write-enabled mode. Import
analysis and export are read-only but may create temporary artifacts outside canonical user data.

Migration tools are never included in ordinary planning or Knowledge mode tool sets. They are exposed
only during an explicit migration workflow, keeping the active migration set to these three tools plus
only the minimum read tools needed to inspect results.

A minimal authenticated UI should provide bundle upload, analysis review, execution confirmation,
and export download. It does not need to edit documents.

## Build order

1. Define and version the manifest schema.
2. Select and verify Markdown/frontmatter parsing with unknown-field preservation.
3. Implement secure streaming ZIP staging and expiry cleanup.
4. Implement analysis with normalized paths, checksums, conflicts, and link reports.
5. Implement atomic execution with stale-analysis detection.
6. Implement deterministic single-document and full export.
7. Add the minimal upload/review/download UI and the three isolated MCP tools.
8. Run repeated fixture and realistic-corpus round trips.
9. Perform the explicit cutover review.

## Tests

- ZIP staging rejects traversal, symlinks, duplicate paths, and size-limit violations.
- Unknown frontmatter fields and original Markdown bodies are preserved.
- Analysis performs no canonical-data mutation.
- Execute rejects expired tokens and stale source or target state.
- Execute is atomic and idempotent.
- Repeated import creates no duplicate documents, revisions, or relations.
- Export ordering, encoding, line endings, ZIP metadata, and manifest serialization are deterministic.
- Two exports of unchanged state have identical canonical contents.
- Export followed by clean import preserves complete document and revision state.
- Account export includes Knowledge tables, and account deletion removes Knowledge data and indexes.
- All operations remain isolated between per-user databases.
- Ordinary chat modes never receive import or export declarations.

## Cutover acceptance criteria

- [ ] Every source Markdown file maps to exactly one document or an approved exclusion.
- [ ] Unknown frontmatter and custom metadata are preserved.
- [ ] Snapshots, living documents, and pipeline documents retain their intended lifecycle.
- [ ] Every resolvable link is represented without altering the source body.
- [ ] Broken and ambiguous links appear in the analysis report.
- [ ] Repeated import creates no duplicate document or relation.
- [ ] Full export produces readable stable paths and complete history.
- [ ] A second unchanged export produces byte-identical canonical content.
- [ ] Export A can be imported into a clean user database to produce equivalent export B.
- [ ] Exact-text search finds all baseline `ripgrep` control matches.
- [ ] Tenant-isolation, account-export, and account-deletion tests pass.
- [ ] The full CI-equivalent build and test sequence passes in Release configuration.

Markdown/git remains authoritative until every accepted criterion passes. Moving the authoritative
master to OwnPlanner is a separate recorded decision, not an automatic consequence of a successful
import.

## Definition of shipped

Part 3 ships only when the round trip and cutover criteria pass, reference documentation describes
the implemented Knowledge model and tools, an ADR records the final design and deviations, and all
three Knowledge plans have been archived according to the documentation process.

## Related documents

- [`knowledge-core-plan.md`](knowledge-core-plan.md) — Part 1 foundation.
- [`knowledge-retrieval-plan.md`](knowledge-retrieval-plan.md) — Part 2 retrieval and organization.
- [`database-schema.md`](../database-schema.md) — per-user persistence model.
- [`adr/0006-gdpr-account-export.md`](../adr/0006-gdpr-account-export.md) — complete account export.
- [`adr/0007-gdpr-account-deletion.md`](../adr/0007-gdpr-account-deletion.md) — account erasure.
