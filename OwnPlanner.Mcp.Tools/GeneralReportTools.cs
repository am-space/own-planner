using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class GeneralReportTools(IGeneralReportReader reader)
{
	[McpServerTool(Name = "general_report_get", Idempotent = true, ReadOnly = true), Description("Get a bounded current-state UTC snapshot. Today counts focus dates, including currently completed tasks. Incomplete deadline buckets are disjoint: overdue before today, due today, upcoming [tomorrow, today+8 days). Inbox captures are notes currently in the active system Inbox; unscheduled Inbox tasks have neither focus nor due dates. Task samples are deduplicated by ID. No note bodies or descriptions; refresh targeted data after changes. Opt in with calendarPeriod=thisWeek (or another named period) for a separate calendar summary with local today, requested-period counts and bounded samples. Legacy UTC fields keep their meanings. Use the calendar section for local-calendar questions; refresh on every such request.")]
	public async Task<object> GetGeneralReport(CancellationToken cancellationToken = default, string? calendarPeriod = null)
	{
		try
		{
			return calendarPeriod is null ? await reader.GetAsync(cancellationToken) : await reader.GetCalendarAsync(calendarPeriod, cancellationToken);
		}
		catch (ArgumentException ex) { return new { error = ex.Message }; }
	}
}
