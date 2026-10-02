using System.ComponentModel;
using ModelContextProtocol.Server;
using OwnPlanner.Application.Calendar;

namespace OwnPlanner.Mcp.Tools;

[McpServerToolType]
public sealed class PlanningCalendarTools(IPlanningCalendar calendar)
{
	[McpServerTool(Name = "calendar_period_get", Idempotent = true, ReadOnly = true), Description("Resolve a named user-calendar period against the current clock and configured timezone/week start, without opening a review or changing preferences/reminders. period is today, thisWeek, remainderOfThisWeek, nextWeek, lastWeek, nextSevenDays or lastSevenDays. Returns calendar dates, half-open UTC boundaries and any explicit UTC fallback. lastSevenDays means the rolling 168 hours ending now; its focus date range covers local dates touched by that interval. Ask the user about ambiguous ranges.")]
	public async Task<object> GetPeriod(string period = "today", CancellationToken cancellationToken = default)
	{
		try { return await calendar.ResolveAsync(period, cancellationToken); }
		catch (ArgumentException ex) { return new { error = ex.Message }; }
	}
}
