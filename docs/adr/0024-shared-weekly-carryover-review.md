# ADR-0024: Shared local-calendar weekly carryover review

**Date:** 2026-09-21

**Status:** Accepted

**Deciders:** OwnPlanner maintainers

## Context

[#62](https://github.com/am-space/own-planner/issues/62) requires opt-in weekly Telegram reminders
and one carryover workflow shared with web chat. Existing weekly reports have a seven-day UTC
contract, focus dates are stored as UTC-labelled calendar values, and account linking is central
while planning data is isolated per user. Deadline removal is supplied by
[ADR-0023](0023-explicit-task-deadline-clearing.md). The
[implementation plan](../archive/weekly-carryover-review-plan.md) records the acceptance mapping.

## Decision

Application owns calendar calculations, validated preferences, review eligibility and lifecycle,
counts-only notification text, and guarded task actions. Infrastructure owns SQL counts/pages,
transactional persistence and delivery dispatch. A web-host worker supplies trusted active linked
accounts and creates initialized user scopes. Dispatch processes at most four users concurrently,
with independent scopes and cancellation, so one slow send does not block every subsequent user.
It rechecks the original link before delivery.

Preferences and review state are Application workflow snapshots, not Domain planner entities.
Infrastructure owns separate `WeeklyReviewPreferencesRow` and `WeeklyReviewRow` persistence models
and maps them explicitly to the snapshots. This preserves the singleton integer key and existing
wire shapes without introducing exceptions to Domain's `EntityBase` convention. The generated
`SeparateWeeklyReviewPersistenceModels` migration updates the EF model snapshot with no SQL changes.
Reading settings returns detached data/defaults without inserting rows and is advertised as read-only
and idempotent in MCP metadata.

`AddWeeklyReviews` adds preferences and review rows to AppDbContext. No planning state or task
content is stored in AuthDbContext. Timezones require explicit selection. Weeks use local calendar
boundaries; DST gaps advance to the first valid minute and overlaps use the earlier instant. Focus
dates retain their stored date meaning. Reports deduplicate carryover, overdue and target-week
commitments, exclude completed/trashed/archived-list tasks, and return exact SQL counts with bounded
pages. Existing UTC report contracts remain unchanged.

A review freezes its target calendar period. Preference changes reuse overlapping calendar weeks
to suppress duplicates, including finished periods. Delivery claims and chat offers are separate
from review completion. Explicit deferral starts another occurrence on the same review and expires
at target-week end. Ordinary missed reminders are never replayed after the target week starts.
SQLite transactions reserve each occurrence before sending. Only explicit HTTP 429 rejections can
retry (three total attempts, 15-minute spacing); ambiguous failures and crashed claims are not resent.
Chat can offer an undelivered eligible review once, with a five-minute grace period for pending sends.

Additive shared MCP tools and authenticated HTTP endpoints expose settings and lifecycle. General's
compact baseline adds the contextual offer tool; weekly_planning exposes the full workflow in General
and Week Planning. Other mode/delegation permissions are preserved. Settings and Telegram `/review`
commands expose explicit lifecycle controls without switching modes. `weekly_review_apply` refreshes
the task/list and checks its reported revision before using the existing task service, including
#61 deadline clearing. It reports per-task failures and never performs automatic rollover.

## Consequences

### Positive

- Calendar semantics, preferences and review state are consistent across delivery channels.
- Tenant data follows existing trusted host scoping and export/deletion behavior.
- Notifications require no model call and include no titles, descriptions or note bodies.
- New contracts are additive and default to disabled reminders.

### Negative / Trade-offs

- Remote delivery and model-rendered chat offers are best-effort: a crash or lost response can lose
  an invitation. Claims favor suppression of duplicate sends over automatic ambiguous retries.
- Preference changes apply to new non-overlapping reviews; an existing period retains its calendar
  and scheduled time. Old state is retained as planner data.
- Revision checks precede existing service calls; they do not introduce a cross-operation lock or
  batch transaction. Concurrent edits after the check and partial batch failure remain possible.
- Suitability of a conversational fallback is governed by model instructions; deterministic tests
  verify the tool, persistence and scripted provider path, not every live model interpretation.

## Related Files

| File | Role |
| --- | --- |
| `OwnPlanner.Application/WeeklyReviews/WeeklyReviewState.cs` | Workflow snapshots and public result shapes |
| `OwnPlanner.Infrastructure/WeeklyReviews/WeeklyReviewPersistenceModels.cs` | Per-user persistence rows and explicit snapshot mapping |
| `OwnPlanner.Application/WeeklyReviews/` | Calendar, service, task action guards and host contracts |
| `OwnPlanner.Infrastructure/WeeklyReviews/` | Transactional store and delivery dispatcher |
| `OwnPlanner.Mcp.Tools/WeeklyReviewTools.cs` | Shared AI contracts |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/WeeklyReminderHost.cs` | Trusted scheduler scope and worker |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Services/WeeklyReviewTelegramHandler.cs` | Explicit Telegram controls |
| `OwnPlanner.Web/OwnPlanner.Web.Server/Controllers/WeeklyReviewController.cs` | Authenticated settings/review endpoints |
| `OwnPlanner.Web/ownplanner.web.client/src/components/settings/WeeklyReviewSettings.tsx` | Settings and review UI |
| `docs/weekly-review.md` | Detailed calendar, retry, lifecycle and API behavior |
