using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class GeneralReportTools(IGeneralReportReader reader)
{
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
