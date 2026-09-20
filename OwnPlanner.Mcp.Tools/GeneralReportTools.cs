using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class GeneralReportTools(IGeneralReportReader reader)
{
	[McpServerTool(Name = "general_report_get", Idempotent = true, ReadOnly = true), Description("Get a bounded current-state UTC snapshot. Today counts focus dates, including currently completed tasks. Incomplete deadline buckets are disjoint: overdue before today, due today, upcoming [tomorrow, today+8 days). Inbox captures are notes currently in the active system Inbox; unscheduled Inbox tasks have neither focus nor due dates. Task samples are deduplicated by ID. No note bodies or descriptions; refresh targeted data after changes.")]
	public async Task<object> GetGeneralReport(CancellationToken cancellationToken = default) =>
		await reader.GetAsync(cancellationToken);
}
