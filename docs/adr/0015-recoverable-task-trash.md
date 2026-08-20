# ADR-0015: Recoverable task deletion with Trash

**Date:** 2026-08-20  
**Status:** Accepted  
**Deciders:** OwnPlanner maintainers

---

## Context

Task deletion previously removed the database row immediately. That made accidental manual deletion
irreversible and prevented delegated automation from safely participating in task cleanup. A prompt
instruction or model-supplied confirmation cannot authorize permanent data loss. Recoverable tasks
must also survive deletion of their original task list without weakening per-user database isolation.

## Decision

`TaskItem` records a nullable UTC `TrashedAt` and retains its logical `TaskListId`. A separate nullable
`ActiveTaskListId` owns the database relationship: active tasks point at their list and retain the
existing cascade behavior; trashing clears that relationship so a recoverable task survives later
list deletion. Restore verifies that the remembered list still exists before reconnecting it.

Normal repositories, planner workspace reads, and all planning report readers explicitly exclude
trashed rows. Dedicated paged Trash reads are opt-in. `TaskItemService.DeleteAsync` becomes an
idempotent soft-delete operation, restore requires a trashed task and valid destination, and permanent
deletion requires a task already in Trash.

The existing `taskitem_delete` MCP name and result shape remain compatible but its description and
behavior now mean “move to Trash.” Additive `taskitem_list_trash` and `taskitem_restore` tools expose
recovery. Permanent deletion is not exposed through MCP or delegated agents; the authenticated web
Trash page invokes a guarded HTTP operation only after a user confirmation dialog.

## Consequences

### Positive

- Accidental deletion is recoverable without losing task state.
- Existing MCP callers receive safer semantics without a tool-name break.
- Trashed tasks remain isolated in the same per-user database and disappear from normal planning.
- Deleting an original list cannot cascade-delete tasks already in Trash.

### Negative / Trade-offs

- Task-list association has both logical and active columns, which must remain synchronized for
  active tasks.
- Restore can fail when the original list has been deleted; OwnPlanner reports this rather than
  guessing another destination.
- Trash has no automatic retention or bulk empty operation.

## Alternatives Considered

- **EF global query filter** — rejected because opt-in Trash and administrative paths would rely on
  implicit bypass behavior. Explicit filters keep each query's intent visible and testable.
- **Keep the existing cascade foreign key** — rejected because deleting a list would silently destroy
  recoverable tasks.
- **Expose permanent deletion as an MCP tool with a confirmation boolean** — rejected because a model
  argument is not evidence of user consent.

## Deferred

Automatic retention, bulk empty Trash, trash for other entity types, and delegated soft deletion are
separate product decisions. Delegated completion and soft deletion are tracked by issue #44.

## Related Files

| File | Role |
|---|---|
| `OwnPlanner.Domain/Tasks/TaskItem.cs` | Trash state and domain transitions |
| `OwnPlanner.Application/Tasks/TaskItemService.cs` | Guarded trash, restore, and permanent-delete use cases |
| `OwnPlanner.Infrastructure/Repositories/TaskItemRepository.cs` | Explicit active/Trash query semantics |
| `OwnPlanner.Mcp.Tools/TaskItemTools.cs` | Compatible soft-delete and additive recovery tools |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/PlannerController.cs` | Authenticated Trash HTTP operations |
| `OwnPlanner.Web/ownplanner.web.client/src/pages/TrashPage.tsx` | User-visible restore and confirmed permanent deletion |
