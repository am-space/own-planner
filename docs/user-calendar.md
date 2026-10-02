# User calendar and planning periods

General, Week Planning, Day Work and Reflection use the timezone and week-start day explicitly
selected in weekly review settings. Reminders can remain disabled. Calendar reads never enable
reminders, write settings, open a review or consume review guidance.

If no timezone is selected, ordinary reports use UTC and return `fallbackExplanation` asking the
user to select a timezone. The stored week start is retained; unconfigured settings default to
Monday. Chat is instructed to disclose this fallback. No timezone is inferred from the browser,
server or language. Manual weekly review still requires an explicit timezone.

| `calendarPeriod` | Meaning |
| --- | --- |
| `today` | Current local calendar date |
| `thisWeek` | Complete calendar week containing today, using the configured week start |
| `remainderOfThisWeek` | Today through the last day of the current calendar week, including today |
| `nextWeek` | Complete calendar week immediately after this week |
| `lastWeek` | Complete calendar week immediately before this week |
| `nextSevenDays` | Seven local calendar dates beginning today |
| `lastSevenDays` | Rolling 168 hours ending at the current instant; focus dates cover the local dates touched by that interval |

Ranges include their start and exclude their end. The last day of `thisWeek` stays in the current
week. Weekly review's rule of targeting next week on the final day applies only to that workflow.
An ordinary report does not reuse its target or frozen boundaries. "After today" excludes today;
chat must distinguish this from a remainder including today and clarify ambiguous requested ranges.

Focus dates retain their stored calendar-date meaning: a UTC-labelled `FocusAt` of Monday stays
Monday in every timezone. Deadlines, completion times, creation and update times are instants,
compared against the selected local boundaries converted to UTC. For calendar windows, local
midnight gaps advance to the first valid minute; repeated wall times choose the earlier instant.
A DST week can contain 167 or 169 hours. `lastSevenDays` deliberately remains 168 elapsed hours,
so its partial first/last local dates can differ from a seven-date calendar window. An exact midnight
exclusive end excludes the newly starting date from focus membership.

## Mode behavior and freshness

| Mode | Explicit preload |
| --- | --- |
| General | `general_report_get calendarPeriod=thisWeek` |
| Week Planning | `weekly_report_get calendarPeriod=thisWeek` |
| Day Work | `taskitem_list_by_focus_date calendarPeriod=today` |
| Reflection | `reflection_report_get calendarPeriod=lastWeek` |

Each turn obtains lightweight `calendar_period_get period=today` metadata from the current clock
and current preferences. Application passes it to the model as request context rather than replayed
history; it refreshes after compaction and does not fetch planner entities. Report/focus calls also
resolve the current clock and read current planning data every time. Chat instructions require a
fresh report for a new date-scoped request, with the requested named period, and actual dates/timezone
in its answer. Natural-language interpretation and tool selection remain model behavior, verified
with scripted orchestration tests rather than claimed as live-model evaluation.

Day Work remains focused on focus plans; this feature does not add deadline tasks. Reflection still
classifies currently incomplete tasks by current focus membership and preserves its historical
limitations; it cannot reconstruct reopened completions or prior assignments.

## Compatible MCP contracts

All hosts use shared handlers against the host-bound user database. No argument selects a user,
database path or timezone belonging to another account. No route or database migration is added.

- `calendar_period_get` is a new read-only/idempotent lookup accepting optional `period` (default
  `today`). It returns the period name, clock, timezone, week start, local today/start/end dates,
  UTC instant boundaries, rolling marker and nullable fallback explanation.
- `weekly_report_get` and `reflection_report_get` add optional `calendarPeriod`. Omission preserves
  their existing UTC defaults and fields. Named-period results add `calendar` metadata and report
  the selected timezone/semantics. Do not combine a named period with `startDate`, `endAtUtc` or a
  non-default `periodDays`; conflicting inputs return an error. Sample limits remain unchanged.
- `taskitem_list_by_focus_date` accepts only `calendarPeriod=today`, without `focusDate`. The local
  response retains `items`, `totalCount`, `offset`, `limit`, `hasMore` and adds `calendar`. Omission
  retains the old UTC-today/explicit-focusDate path and result shape.
- `general_report_get` adds optional `calendarPeriod`. It preserves every legacy UTC field,
  including rolling upcoming commitments. Its separate `calendar` summary contains local today
  focus/completed counts, today's deadlines, current overdue deadlines, requested-period focus/due
  counts, exact distinct incomplete task count and bounded samples. Today retains at most five
  remaining IDs; the period retains at most five IDs; their shared calendar sample table deduplicates
  IDs and clips titles to 80 UTF-16 units. Do not relabel legacy fields as local calendar values.

General's summary is the calendar foundation for [#74](https://github.com/am-space/own-planner/issues/74).
Its attention sections, selection-reason annotations and drill-down behavior remain separate work.
See [ADR-0029](adr/0029-shared-user-calendar.md) and [weekly review](weekly-review.md).
