# General attention overview

General's broad “What needs my attention?” request uses a fresh `general_attention_get` read.
The default overview covers the user's current calendar week, including its final day. Calendar
preferences, UTC fallback, focus-date semantics and DST boundaries follow [user calendar](user-calendar.md).
The read never writes preferences, opens a weekly review or changes tasks.

## Sections and reasons

| Section | Selection | Default display precedence |
| --- | --- | --- |
| `today` | Incomplete focus plans for today or deadlines within today's local day | First, main section |
| `earlierThisWeek` | Incomplete focus plans from this week's start through yesterday | After today |
| `remainingWeek` | Focus plans or deadlines from tomorrow through this week's final day | After earlier plans, grouped by display date |
| `overdue` | Deadlines strictly before the captured `asOfUtc`, regardless of age | After week sections |
| `olderFocus` | Incomplete focus plans before this week's start | Last, count only by default |

The default `all` view assigns each task to the first matching section in this order. A deadline
expired earlier today therefore remains in today, with `overdueDeadline` among its reasons. A task
with an earlier-week focus date and a future deadline is displayed with earlier plans while retaining
both annotations. Flexible missed focus plans are never labelled expired deadlines. Tasks outside
these reason sets are omitted. Completed tasks, Trash and archived task lists are excluded.

Each sample includes a clipped title, stored `focusDate`, UTC `dueAt`, local `dueDate`, `displayDate`
and every applicable reason: `plannedToday`, `dueToday`, `missedFocusThisWeek`,
`plannedLaterThisWeek`, `dueLaterThisWeek`, `overdueDeadline`, `olderFocus`. Remaining-week
`displayDate` is the earlier relevant future focus/deadline date; both dates remain visible.

`matchCount` counts every task matching the section's reasons, including tasks assigned elsewhere.
`totalCount` counts the section's assigned tasks in the overview. Do not sum overlapping match
counts. Each week's section has exact `days` counts by display date independently of samples;
overdue/olderFocus omit day breakdowns to keep arbitrarily old backlogs bounded. The default samples
contain at most five tasks per section and zero for olderFocus, with at most twenty unique tasks.
Title clipping is at most 80 UTF-16 units and preserves surrogate pairs. No descriptions or note
bodies are retrieved for attention.

## Refresh and drill-down

`general_attention_get` accepts optional `section` (default `all`), `offset` (default 0), and `limit`
(default 5, range 1–20). `all` accepts only offset 0. A named section returns **every matching task**,
including tasks assigned elsewhere in the overview, in stable date/deadline/focus/ID order. Its
`totalCount` equals `matchCount`; only that section and its sample table are returned. This makes
all overdue work accessible even when default today/earlier/future samples are truncated.

`truncated` means the returned page omits any selected tasks, including preceding pages. `hasMore`
means additional tasks follow this page. Request the next page with the same section/limit and
an offset advanced by the returned count. These are fresh current-state pages, not frozen cursors;
concurrent changes can shift membership. Deduplicate IDs when combining separate reason queries.

For explicitly day-scoped attention, General uses `section=today`; focus-only requests may use
`general_report_get calendarPeriod=today`. General does not turn a day request into a weekly
briefing. It states when today is empty, omits empty secondary sections, discloses truncation and
presents older plans as a compact count with optional drill-down. Exploratory chat does not trigger
an automatic overview. Each new attention request, including after completion, reads fresh data.
Tool choice and presentation are model instructions; tests use scripted provider calls, not live
Gemini evidence.

## Compatibility and delivery

`general_report_get calendarPeriod=thisWeek` adds `calendar.attention`, the same default overview,
for the existing General preload. Other named periods leave attention null. Existing UTC fields,
rolling commitments, calendar counts/samples and all existing tool arguments retain their meanings.
The initial preload remains a dated snapshot. `general_attention_get` is available immediately in
General, with no skill load, and does not broaden specialist modes' permissions.

Both operations use the existing tenant-bound General reader and shared MCP tool class in the web
in-process adapter, HTTP MCP and stdio host. There are no tenant selectors, new HTTP routes,
dependencies or database migrations. See [ADR-0030](adr/0030-general-week-attention.md).
