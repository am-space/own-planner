using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class StrategicReportTools(IStrategicReportReader reader)
{
	[McpServerTool(Name = "strategic_report_get", Idempotent = true, ReadOnly = true), Description("Get a compact deterministic strategic snapshot of active goals, non-archived contexts, lists, tasks, notes, and structural planning signals. Active tasks means incomplete, non-trashed tasks in non-archived lists; no active tasks does not mean no linked tasks ever or historical inactivity. TasksWithoutGoal includes null or missing goal references, not links to existing inactive goals. Empty contexts and unlinked operational tasks are observations, not established defects or urgency. Sample limits default to 3 tasks and 2 notes and must be between 0 and 5. Samples are bounded, previews may be truncated, and counts cover the selected scope. Use entity get/list tools for material drill-down and fresh evidence when current state matters.")]
	public async Task<object> GetStrategicReport(
		int taskSampleLimit = 3,
		int noteSampleLimit = 2,
		CancellationToken cancellationToken = default)
	{
		try
		{
			return await reader.GetAsync(
				new StrategicReportOptions(taskSampleLimit, noteSampleLimit),
				cancellationToken);
		}
		catch (ArgumentOutOfRangeException ex)
		{
			return new { error = ex.Message };
		}
	}
}
