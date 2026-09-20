> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0021: General default chat with bounded initial context](../adr/0021-general-default-chat.md).
> Details below may not reflect later refinements made during review.

# General default chat — implementation plan

Status: Implemented in the #54 pull request. Issue: #54. Dependency #53 is merged.

Outcome: new chats offer an everyday General assistant, with a bounded UTC snapshot and on-demand skills and direct agents.

## Acceptance mapping and impact

- Application: General default, five initial tools, only General report preloaded, discussion-before-write and freshness instructions, starter prompts. Test initialization, switching, repeated turns and compaction; retain existing mode boundaries and enum values.
- Application reporting: immutable report contract and deterministic composition rules. Counts are exact; today includes currently completed focus tasks, commitments include only incomplete tasks in disjoint UTC calendar buckets (before today, today, tomorrow through seven days after today). Test midnight/year boundaries, exclusions, samples, stable ties, deduplication and title bounds.
- Infrastructure: tenant-bound read-only report reader, projecting metadata without descriptions/note bodies, single read transaction. Test real SQLite, empty data, cancellation, tenant isolation. New Telegram links default General; existing persisted choices and /new selections survive.
- MCP and web: additive parameterless general_report_get shared by in-process, HTTP MCP and stdio registration. Test schema/dispatch and two authenticated adapters. No new HTTP route or changed result shapes for existing tools.
- Frontend: General selector, labels, defaults/reset/fallback, preserve current explicit mode. Browser coverage for initial mode, switching and reset. Console inherits Application default and existing enum menu.
- Domain/persistence schema: no changes or migrations. Existing UTC timestamps are the trustworthy basis; no timezone preference subsystem.
- Documentation: reference report predicates and bounds, Telegram behavior, ADR and archived plan.

## Report design

Task eligibility excludes Trash and archived lists, retains missing-list tasks like other reports. Today counts all eligible tasks focused in [today,tomorrow), including currently completed tasks (not historical completion events). Show at most five remaining focus references. Deadline counts are disjoint calendar buckets; show three nearest deadlines across them. A shared task sample table deduplicates references, maximum eight entries, titles capped at 80 characters. Direction counts all active goals and samples at most three linked to today's focus or today's/upcoming commitments. Inbox captures mean notes currently in the active system Inbox; unscheduled Inbox tasks are incomplete with neither focus nor due date. Samples expose IDs, explicit limits and truncation indicators; never descriptions/bodies. Goal titles capped at 80 characters. Aim for 600–1,000 approximate tokens on a representative fixture, not a universal bound.

## Order and verification

1. Add Application report builder/contract and Infrastructure reader following existing report reader factory/transaction patterns.
2. Add shared tool/registrations; wire General policy/preload and entry-point defaults.
3. Add focused report, orchestration, Telegram, tenant and browser tests.
4. Run ./scripts/verify.sh --all; inspect full diff and acceptance mapping; write ADR and archive plan using git mv; commit, push and open PR closing #54.

## Plan review

Reviewed before implementation: all issue criteria mapped above; inner-layer composition avoids transport business rules; tenant identity remains host-owned; tool addition is backward compatible; enum numbering unchanged; no migrations, auto-switching, live provider dependency or destructive capabilities. Preserve Telegram persisted mode even on /new as the explicit-selection exception to default resets. Existing #53 provider tests cover direct agents and skill lifecycle; augment General discussion/write scenarios without claiming deterministic tests prove live model compliance.
