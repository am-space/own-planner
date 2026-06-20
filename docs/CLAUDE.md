# docs/ — Documentation process

Guidance for working with documentation in this folder. The root `CLAUDE.md` has the short version;
this file holds the mechanics.

## Three kinds of docs

| Kind | Location | Lifecycle |
|---|---|---|
| **Plan** | `docs/*-plan.md` | Written *before* building. A working document; expected to drift from final code. Archived once shipped. |
| **ADR** | `docs/adr/NNNN-kebab-title.md` | Durable record of a decision *as shipped*. Never edited to fit later decisions — superseded by a new ADR instead. |
| **Reference** | `docs/*.md` (e.g. `architecture-layers.md`, `email-configuration.md`) | Living description of how things work now. Update it in the same change that alters the behavior. |

## Writing an ADR

1. Copy `docs/adr/template.md` to `docs/adr/NNNN-<kebab-title>.md`, where `NNNN` is the next unused
   number (zero-padded; current highest is `0003`).
2. Fill in the header (`Date` = today, `Status` = `Accepted` once the work merges, `Deciders`).
3. Describe the decision **as actually built**, not as originally planned — fold in any refinements
   made during review. The `Decision` section should match the code; the `Related Files` table
   points at the implementation.
4. When an ADR replaces an earlier one, set the old ADR's `Status` to `Superseded by ADR-NNNN` and
   the new one's `Context` should link back.

## Archiving a plan when its feature ships

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
