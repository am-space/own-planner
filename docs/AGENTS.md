# docs/ — Documentation process

Guidance for working with documentation in this folder. The root `AGENTS.md` has the short version;
this file holds the mechanics.

Keep [`README.md`](README.md) current when adding, moving, renaming, or removing documentation.

## Four kinds of docs

| Kind | Location | Lifecycle |
|---|---|---|
| **Backlog proposal** | `docs/backlog/*-plan.md` | Potential work with no implementation commitment. Promoted to an active plan when the team decides to build it. |
| **Active plan** | `docs/*-plan.md` | Approved or actively pursued implementation work. Expected to drift from final code; archived once shipped. |
| **ADR** | `docs/adr/NNNN-kebab-title.md` | Durable record of a decision *as shipped*. Never edited to fit later decisions — superseded by a new ADR instead. |
| **Reference** | `docs/*.md` (e.g. `architecture-layers.md`, `email-configuration.md`) | Living description of how things work now. Update it in the same change that alters the behavior. |

## Promoting a backlog proposal

1. Confirm that the work is approved or actively being pursued.
2. Change its `Status` from `Backlog` to `Active`.
3. `git mv docs/backlog/<feature>-plan.md docs/<feature>-plan.md`.
4. Update and verify any relative links affected by the move.

## Writing an ADR

1. Copy `docs/adr/template.md` to `docs/adr/NNNN-<kebab-title>.md`, where `NNNN` is the next unused
   zero-padded number.
2. Fill in the header (`Date` = today, `Deciders`). Set `Status: Accepted` in the implementation
   pull request so the ADR lands as accepted when that pull request merges; do not leave a shipped
   decision as `Proposed` on `master`.
3. Describe the decision **as actually built**, not as originally planned — fold in any refinements
   made during review. The `Decision` section should match the code; the `Related Files` table
   points at the implementation.
4. When an ADR replaces an earlier one, set the old ADR's `Status` to `Superseded by ADR-NNNN` and
   the new one's `Context` should link back.

## Archiving a plan when its feature ships

Perform the ADR update and plan archive in the implementation pull request. They describe the
result that the pull request will ship and become canonical when it merges. If the pull request does
not merge, the active plan on `master` remains active.

1. Write or update the corresponding ADR first.
2. `git mv docs/<feature>-plan.md docs/archive/<feature>-plan.md`.
3. Add an archival banner at the top of the moved plan:

   ```markdown
   > **Archived — implemented.** This is the original implementation plan, kept for historical
   > context. The shipped design and rationale are recorded in
   > [ADR-NNNN: <title>](../adr/NNNN-<kebab-title>.md).
   > Details below may not reflect later refinements made during review.
   ```

**Never delete a plan** — archive it. And never leave a shipped plan sitting in `docs/` as if it were
current documentation; that's what the ADR + archive split prevents.
