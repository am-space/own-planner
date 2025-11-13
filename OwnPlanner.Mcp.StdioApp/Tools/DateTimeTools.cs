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

	[McpServerTool(Name = "datetime_get_timezone_info", Idempotent = true, ReadOnly = true), Description("Get information about the server's local timezone.")]
	public Task<object> GetTimezoneInfo()
	{
		var tz = TimeZoneInfo.Local;
		var now = DateTime.Now;
		
		return Task.FromResult<object>(new
		{
			id = tz.Id,
			displayName = tz.DisplayName,
			standardName = tz.StandardName,
			daylightName = tz.DaylightName,
			baseUtcOffset = tz.BaseUtcOffset.ToString(@"hh\:mm"),
			supportsDaylightSavingTime = tz.SupportsDaylightSavingTime,
			isDaylightSavingTime = tz.IsDaylightSavingTime(now),
			currentOffset = tz.GetUtcOffset(now).ToString(@"hh\:mm")
		});
	}

	[McpServerTool(Name = "datetime_format", Idempotent = true, ReadOnly = true), Description("Format a given date string into various common formats. Provide dateString in ISO 8601 format (e.g., '2024-01-15T10:30:00').")]
	public Task<object> FormatDateTime(string dateString)
	{
		try
		{
			if (!DateTime.TryParse(dateString, out var dateTime))
			{
				return Task.FromResult<object>(new { error = "Invalid date format. Please provide a valid date string." });
			}

			return Task.FromResult<object>(new
			{
				success = true,
				original = dateString,
				formats = new
				{
					iso8601 = dateTime.ToString("o"),
					shortDate = dateTime.ToString("d"),
					longDate = dateTime.ToString("D"),
					shortTime = dateTime.ToString("t"),
					longTime = dateTime.ToString("T"),
					fullDateTime = dateTime.ToString("f"),
					rfc1123 = dateTime.ToString("r"),
					sortable = dateTime.ToString("s"),
					universal = dateTime.ToUniversalTime().ToString("u"),
					yearMonth = dateTime.ToString("Y"),
					monthDay = dateTime.ToString("M"),
					custom = new
					{
						yyyyMMdd = dateTime.ToString("yyyy-MM-dd"),
						ddMMyyyy = dateTime.ToString("dd/MM/yyyy"),
						MMddyyyy = dateTime.ToString("MM/dd/yyyy"),
						HHmmss = dateTime.ToString("HH:mm:ss"),
						hmmsstt = dateTime.ToString("h:mm:ss tt")
					}
				},
				components = new
				{
					year = dateTime.Year,
					month = dateTime.Month,
					day = dateTime.Day,
					hour = dateTime.Hour,
					minute = dateTime.Minute,
					second = dateTime.Second,
					dayOfWeek = dateTime.DayOfWeek.ToString(),
					dayOfYear = dateTime.DayOfYear
				}
			});
		}
		catch (Exception ex)
		{
			return Task.FromResult<object>(new { error = ex.Message });
		}
	}
}
