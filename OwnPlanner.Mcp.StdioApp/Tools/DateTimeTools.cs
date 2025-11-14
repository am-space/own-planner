using System.ComponentModel;
using ModelContextProtocol.Server;

namespace OwnPlanner.Mcp.StdioApp.Tools;

[McpServerToolType]
public class DateTimeTools
{
	[McpServerTool(Name = "datetime_get_current", Idempotent = true, ReadOnly = true), Description("Get the current date and time in UTC and local timezone. Useful for time-sensitive queries, scheduling, and understanding what time it is now.")]
	public Task<object> GetCurrentDateTime()
	{
		var utcNow = DateTime.UtcNow;
		var localNow = DateTime.Now;
		
		return Task.FromResult<object>(new
		{
			utc = new
			{
				datetime = utcNow.ToString("o"), // ISO 8601 format
				date = utcNow.ToString("yyyy-MM-dd"),
				time = utcNow.ToString("HH:mm:ss"),
				dayOfWeek = utcNow.DayOfWeek.ToString(),
				timezone = "UTC"
			},
			local = new
			{
				datetime = localNow.ToString("o"), // ISO 8601 format
				date = localNow.ToString("yyyy-MM-dd"),
				time = localNow.ToString("HH:mm:ss"),
				dayOfWeek = localNow.DayOfWeek.ToString(),
				timezone = TimeZoneInfo.Local.DisplayName,
				timezoneId = TimeZoneInfo.Local.Id,
				offset = TimeZoneInfo.Local.GetUtcOffset(localNow).ToString(@"hh\:mm")
			},
			timestamp = new DateTimeOffset(utcNow).ToUnixTimeSeconds()
		});
	}
}
