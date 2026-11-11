> **Archived — implemented.** This is the original implementation plan, kept for historical
> context. The shipped design and rationale are recorded in
> [ADR-0033: Dedicated Weekly Review page](../adr/0033-dedicated-weekly-review-page.md).
> Details below may not reflect later refinements made during review.

# Dedicated Weekly Review page

**Status:** Completed
**Issue:** [#85](https://github.com/am-space/own-planner/issues/85)

## Outcome

Make the existing Weekly Review directly accessible in the authenticated planner navigation and put
its preferences first in Settings, without changing shared review semantics.

## Acceptance mapping

| Acceptance | Implementation | Evidence |
| --- | --- | --- |
| Desktop/mobile entry next to Tasks | Shared PlannerShell navigation item and authenticated `/planner/review` route | Browser navigation on both viewport sizes |
| Direct URL and refresh | Route under existing ProtectedRoute; page owns vertical scrolling within shell | Browser direct-load/refresh assertions |
| Open, page, finish, skip, defer | Extract existing workflow and goal/task presentation into WeeklyReviewPage; reuse apiService | Browser pagination and lifecycle checks against real shared API |
| Preferences first; no inline review | Keep preference-only WeeklyReviewSettings before Telegram; Open link navigates | Browser settings order and absence of workflow controls |
| Links and setup/loading/error/empty states | Review settings link targets first Settings section; explicit missing-timezone setup, loading and retry feedback | Browser setup, link, delayed/error response and empty tests |
| Manual review with disabled reminders/no Telegram; no task edits | Gate opening only on explicit timezone; retain existing APIs without task mutation calls | Browser disabled-reminder flow and unchanged task evidence |
| Current documentation | Update weekly-review and planner workspace references; record ADR and archive this plan | Documentation/diff review |

## Impact map

- Frontend: affected (routing, navigation, settings, extracted review page).
- Tests: affected (existing Playwright E2E suite).
- Documentation: affected (references, ADR, plan/index).
- Domain, Application, Infrastructure, Web API, MCP and console/stdio: not applicable; existing
  review APIs, lifecycle, tenant boundaries, calendar selection and guidance claims are reused.
- Database migrations and compatibility work: none; additive frontend route only.

## Order and patterns

1. Split the current WeeklyReviewSettings component; retain its goal guidance and report contents.
2. Add the authenticated route and shared navigation item, keeping the planner assistant and making
   the review surface scroll within the existing bounded shell.
3. Move preference-only settings to the first section and connect both navigation links.
4. Adapt existing WeeklyReviewE2eTests and add focused navigation/lifecycle/state coverage using
   the existing E2ePageTest and real backend fixture.
5. Update references, record the frontend responsibility split in ADR-0033, and archive the plan.

## Verification

Prepare with `./scripts/setup.sh`; run focused WeeklyReviewE2eTests while iterating, then
`./scripts/verify.sh --all` for lint/build, backend tests and the full browser suite. Inspect the
complete diff and acceptance mapping before commit/push and a PR targeting master with Closes #85.

## Exclusions, assumptions and risks

- No new task editing, retrospective reporting, backend APIs or reminder/delivery policy changes.
- Opening the dedicated page presents the review automatically after reading explicit timezone
  preferences. It may advance workflow state and claim existing guidance, but never edits tasks.
- Preserve existing goal-first presentation (including bounded completed-work evidence); this is
  relocation of existing content rather than a new retrospective.
- Initial preference lookup must finish before attempting open; preference failures must not appear
  as loading forever. Preserve current review content when a subsequent action fails.
- The shell bounds the planner outlet height, so the page must provide scrolling on narrow and
  desktop screens. Mobile assistant switching must keep review contents usable when returning.

## Plan review

Reviewed before feature code changes: each acceptance criterion maps to implementation and browser
evidence. The change stays within React presentation and reuses Application-owned business rules.
No tenant selection, HTTP/MCP contract, persistence, secrets or calendar logic changes are needed.
Documentation and browser coverage are proportional to the navigation/workflow move; no unrelated
cleanup is included. No material product decisions remain unresolved.

## Implementation evidence

- WeeklyReviewPage uses existing preference, open and transition APIs; goal/task content was moved
  from the Settings component and retains existing backend guidance and calendar semantics.
- Focused WeeklyReviewE2eTests passed all eight cases, including desktop/mobile links and refresh,
  missing timezone, disabled reminders without Telegram, paging, lifecycle actions, loading/retry,
  authentication, tenant isolation and one-time goal guidance. Task snapshots are unchanged after
  opening, paging and review transitions.
- Full repository verification is recorded in the implementation pull request.
