using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class WeeklyReportTools(IWeeklyReportReader reader)
{
	[McpServerTool(Name = "weekly_report_get", Idempotent = true, ReadOnly = true), Description("Get a compact deterministic seven-day UTC workload report. startDate is an optional ISO date (yyyy-MM-dd); taskSampleLimit defaults to 3 (0-5); overloadedDayThreshold defaults to 5 (1-20). Focus dates are plans, due dates are fixed commitments, and counts remain distinct.")]
	public async Task<object> GetWeeklyReport(
		string? startDate = null,
		int taskSampleLimit = 3,
		int overloadedDayThreshold = 5,
		CancellationToken cancellationToken = default)
	{
		if (startDate is not null && !DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
			return new { error = "startDate must be an ISO UTC calendar date in yyyy-MM-dd format." };

		try
		{
			var parsedStartDate = startDate is null
				? (DateOnly?)null
				: DateOnly.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
			return await reader.GetAsync(new WeeklyReportOptions(parsedStartDate, taskSampleLimit, overloadedDayThreshold), cancellationToken);
		}
		catch (ArgumentOutOfRangeException ex)
		{
			return new { error = ex.Message };
		}
	}
}
