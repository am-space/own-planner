# Day Work daily execution

Day Work preloads `day_report_get`, a compact read-only snapshot of the user's local today.
For each daily attention or priority request, repeated “what is left?” and after changes, its
instructions require a fresh daily read. Exploratory turns do not automatically retrieve a report.
The lightweight request calendar context remains fresh on every turn. Both follow the
[shared user calendar](user-calendar.md), including disclosed UTC fallback and DST boundaries.

## Selection and bounds

The overview has two sections: `today` (incomplete focus plans for today or deadlines within today's
local day), followed by `overdue` (incomplete deadlines strictly before `asOfUtc`). Tasks due today
qualify with absent or differing focus dates. Focus dates retain stored calendar-date semantics;
deadlines are UTC instants with local `dueDate`. Completed tasks, Trash and archived lists are excluded.

Each task appears once in the default `all` view, assigned to today first, retaining every applicable
`plannedToday`, `dueToday`, `overdueDeadline` reason. A deadline expired earlier today therefore
stays in today with its overdue annotation. `matchCount` counts every reason match, including tasks
assigned elsewhere; `totalCount` counts assigned tasks. Do not sum overlapping match counts.

The default limit is five per section, at most ten unique samples. Titles are clipped to 80 UTF-16
units without splitting surrogate pairs; no descriptions or note bodies are loaded. Each sample
includes ID, title/clipping marker, importance, stored focus date, UTC deadline, local deadline date
and all daily reasons. Samples sort by deadline, importance, focus date and ID for deterministic
retrieval. This is not a capacity ranking or a decision about what the user must do first.

## Drill-down and prioritization

`day_report_get` accepts optional `section` (`all`, `today`, `overdue`), `offset` (non-negative, default
0), and `limit` (1–20, default 5). The daily period is resolved internally and cannot be overridden.
`all` requires offset 0. A named section pages all matching tasks, including tasks assigned elsewhere
in the overview; its total equals its match count. `truncated` means any selected tasks were omitted,
including preceding pages; `hasMore` means tasks remain after this page. Advance offset by the
returned count. These are fresh current-state pages, not frozen cursors; concurrent changes can shift
membership. Deduplicate IDs when combining reads.

Present today as the main section and overdue work as a short warning/count with bounded examples
and optional pages. State explicitly when no planned or due-today tasks remain. Do not invent a
schedule or automatically bring the backlog into today. Focus-only requests retain
`taskitem_list_by_focus_date calendarPeriod=today`.

For “what first?”, consider deadline timing, importance, today's plan, remaining work and user
constraints. A due-today task need not be first; do not infer lower importance for unsampled tasks.
Retrieve relevant task detail or clarify a necessary missing constraint, without inventing duration
estimates. Use existing execution tools only for requested changes. This report does not authorize
rescheduling, deadline changes, mode switches or weekly review actions.

## Architecture and compatibility

Application's pure `DayReportBuilder` composes the result. The existing tenant-bound report reader
queries eligible metadata and resolves fresh today through `IPlanningCalendar`. The shared
`GeneralReportTools` handler exposes `day_report_get` in web in-process, HTTP MCP and stdio paths.
Only Day Work gains the new read permission; other mode ceilings and all existing writes remain
unchanged. Existing report/task HTTP and MCP shapes retain their semantics. No routes, dependencies
or migrations are added.

Selection, boundaries, exclusions, pagination, completion refresh, cross-user binding and shared
schema are tested. Prompt/routing evidence uses scripted provider responses rather than live Gemini
quality measurements. See [ADR-0031](adr/0031-day-work-deadlines.md).
