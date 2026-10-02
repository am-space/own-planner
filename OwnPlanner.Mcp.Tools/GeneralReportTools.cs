using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class GeneralReportTools(IGeneralReportReader reader)
{
	[McpServerTool(Name = "day_report_get", Idempotent = true, ReadOnly = true), Description("Read fresh daily execution using the user's local today. Incomplete planned-today and due-today tasks appear once with all plannedToday/dueToday/overdueDeadline reasons, importance, stored focus date, UTC deadline and local deadline date. Default section=all shows today first and a bounded overdue warning. MatchCount counts every reason match, including tasks displayed elsewhere; TotalCount counts assigned tasks in all. Never sum overlapping match counts. Samples are bounded; Truncated means tasks are omitted and HasMore means another page follows. For more tasks use section=today or overdue, non-negative offset and limit 1..20: named sections page ALL matches, including those displayed elsewhere. Pages are fresh current state, not frozen cursors; deduplicate IDs when combining reads. Excludes completed, Trash and archived lists. Refresh for each daily attention/priority request or after changes. Sampling order is not execution priority; consider timing, importance, user constraints and remaining work. Read-only, no scheduling or weekly review.")]
	public async Task<object> GetDayReport(string section = "all", int offset = 0, int limit = 5, CancellationToken cancellationToken = default)
	{
		try
		{
			var options = new DayReportOptions(section, offset, limit);
			options.Validate();
			return await reader.GetDayAsync(options, cancellationToken);
		}
		catch (ArgumentException ex) { return new { error = ex.Message }; }
	}

	[McpServerTool(Name = "general_attention_get", Idempotent = true, ReadOnly = true), Description("Read fresh today-first current-week attention using the user's calendar, including this week's final day. Default section=all displays tasks once in priority order: today, earlierThisWeek, remainingWeek, overdue, olderFocus. Tasks retain every applicable reason, stored focus date and local deadline date. MatchCount counts all matching tasks, including those displayed elsewhere; TotalCount counts displayed-section tasks. Samples are bounded; days have exact counts. Older focus plans are count-only by default. For more relevant tasks use a named section (today, earlierThisWeek, remainingWeek, overdue, olderFocus), limit 1..20 and non-negative offset: it pages ALL matching tasks, including those displayed elsewhere in the overview. HasMore signals another page; Truncated signals any omitted tasks. This is a fresh current-state page, not a frozen cursor. Excludes completed, Trash and archived lists. Read-only; never reschedules or opens a weekly review. Refresh for each new attention request or after changes; use section=today for explicitly day-scoped requests.")]
	public async Task<object> GetAttention(string section = "all", int offset = 0, int limit = 5, CancellationToken cancellationToken = default)
	{
		try
		{
			var options = new GeneralAttentionOptions(section, offset, limit);
			options.Validate();
			return await reader.GetAttentionAsync(options, cancellationToken);
		}
		catch (ArgumentException ex) { return new { error = ex.Message }; }
	}

	[McpServerTool(Name = "general_report_get", Idempotent = true, ReadOnly = true), Description("Get a bounded current-state UTC snapshot. Today counts focus dates, including currently completed tasks. Incomplete deadline buckets are disjoint: overdue before today, due today, upcoming [tomorrow, today+8 days). Inbox captures are notes currently in the active system Inbox; unscheduled Inbox tasks have neither focus nor due dates. Task samples are deduplicated by ID. No note bodies or descriptions; refresh targeted data after changes. Opt in with calendarPeriod=thisWeek (or another named period) for a separate calendar summary with local today, requested-period counts and bounded samples. For thisWeek the calendar also includes bounded today-first attention with all selection reasons; use general_attention_get for fresh attention and section pages. Legacy UTC fields keep their meanings. Use the calendar section for local-calendar questions; refresh on every such request.")]
	public async Task<object> GetGeneralReport(CancellationToken cancellationToken = default, string? calendarPeriod = null)
	{
		try
		{
			return calendarPeriod is null ? await reader.GetAsync(cancellationToken) : await reader.GetCalendarAsync(calendarPeriod, cancellationToken);
		}
		catch (ArgumentException ex) { return new { error = ex.Message }; }
	}
}
