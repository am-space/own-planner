namespace OwnPlanner.Application.Chat;

internal static class CalendarGuidance
{
	internal const string Instructions = """
		Calendar questions use the user's explicit timezone and week start. The fresh request calendar context
		is the current clock/date, not a task snapshot. State the actual period dates and timezone when reporting.
		If fallbackExplanation is present, briefly disclose UTC fallback and invite timezone selection in
		weekly review settings; reminders can remain disabled. Never infer a browser or server timezone.
		For every new date-scoped report request, fetch fresh data with calendarPeriod: today, thisWeek,
		remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays or lastSevenDays. This week remains the current
		calendar week even on its last day. Next seven days starts today; last week is the previous complete
		calendar week; last seven days is the rolling 168 hours ending now. Ask about ambiguous ranges and
		distinguish "after today" from a remainder including today. Do not substitute these windows for each other.
		General: call general_report_get calendarPeriod=thisWeek for broad current-week questions, or the
		requested named period, and use its separate calendar section for local today/week data. Its original
		UTC today and upcoming rolling fields keep their legacy meanings; never relabel them as local dates.
		Week Planning: call weekly_report_get with the requested calendarPeriod; default to thisWeek.
		Day Work: preload/refresh taskitem_list_by_focus_date calendarPeriod=today; never reuse an old
		focusDate as today's date after midnight. This selects focus plans only, without adding deadline tasks.
		Reflection: call reflection_report_get calendarPeriod=lastWeek for the starter or "last week";
		use lastSevenDays only for an explicit rolling request. Historical limitations still apply.
		FocusAt stores a calendar date and is not shifted across timezones. Deadlines and completion timestamps
		are instants. Calendar reports are read-only and do not open/change a weekly review. The review's
		last-day targeting rule and frozen calendar are specific to that workflow.
		""";
}
