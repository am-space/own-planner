using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class ReflectionReportTools(IReflectionReportReader reader)
{
	private static readonly Regex UtcIsoPattern = new(
		@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|z|[+-]00:00)$",
		RegexOptions.CultureInvariant);

	[McpServerTool(Name = "reflection_report_get", Idempotent = true, ReadOnly = true), Description("Get a compact deterministic current-state reflection report for a UTC half-open period. periodDays defaults to 7 (1-31); endAtUtc is an optional ISO timestamp with zero UTC offset; task and note sample limits default to 3 (0-5). The result states historical limitations explicitly. Opt in with calendarPeriod (today, thisWeek, remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays, lastSevenDays) for the configured user timezone/week start; omit it to preserve UTC defaults. Do not combine it with legacy date/range overrides. The result includes actual dates, UTC boundaries and any explicit fallback. Focus dates are calendar dates; deadline/completion timestamps are instants. lastSevenDays is a rolling 168-hour range ending now, distinct from lastWeek. Resolve ambiguous ranges with the user.")]
	public async Task<object> GetReflectionReport(
		int periodDays = 7,
		string? endAtUtc = null,
		int taskSampleLimit = 3,
		int noteSampleLimit = 3,
		CancellationToken cancellationToken = default,
		string? calendarPeriod = null)
	{
		if (!TryParseUtc(endAtUtc, out var parsedEndAtUtc))
			return new { error = "endAtUtc must be an ISO timestamp with a zero UTC offset, for example 2026-08-19T12:00:00Z." };

		try
		{
			return await reader.GetAsync(
				new ReflectionReportOptions(periodDays, parsedEndAtUtc, taskSampleLimit, noteSampleLimit, calendarPeriod),
				cancellationToken);
		}
		catch (ArgumentException ex)
		{
			return new { error = ex.Message };
		}
	}

	private static bool TryParseUtc(string? value, out DateTime? result)
	{
		result = null;
		if (value is null) return true;
		if (!UtcIsoPattern.IsMatch(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.Offset != TimeSpan.Zero)
			return false;
		result = parsed.UtcDateTime;
		return true;
	}
}
