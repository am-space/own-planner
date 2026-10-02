using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task SharedCalendarAndLocalReportsUseOnlyAuthenticatedPreferencesAndRefreshAcrossMidnight()
	{
		var ct = TestContext.Current.CancellationToken;
		var clock = new CalendarClock(new DateTime(2026, 8, 19, 14, 59, 0, DateTimeKind.Utc));
		await using var services = BuildTenantServiceProvider(s => s.AddSingleton<TimeProvider>(clock));
		var taskA = await SeedUserTaskIdAsync("calendar-a", "A local task", ct);
		var taskB = await SeedUserTaskIdAsync("calendar-b", "B private task", ct);
		await using var a = CreateAdapter(services, "calendar-a");
		await using var b = CreateAdapter(services, "calendar-b");
		foreach (var (adapter, zone, weekStart, task, date) in new[]
		{
			(a, "Asia/Tokyo", 1, taskA, "2026-08-20"),
			(b, "America/Los_Angeles", 0, taskB, "2026-08-19")
		})
		{
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?>
				{ ["enabled"] = false, ["timeZoneId"] = zone, ["weekStart"] = weekStart }, ct);
			await adapter.CallToolAsync("taskitem_set_focus_date", new Dictionary<string, object?> { ["id"] = task, ["focusDate"] = date }, ct);
		}
		var first = ParseJsonElement(await a.CallToolAsync("taskitem_list_by_focus_date", new Dictionary<string, object?> { ["calendarPeriod"] = "today" }, ct));
		first.GetProperty("calendar").GetProperty("today").GetString().Should().Be("2026-08-19");
		first.GetProperty("items").GetArrayLength().Should().Be(0);
		clock.Now = clock.Now.AddMinutes(2);
		var second = ParseJsonElement(await a.CallToolAsync("taskitem_list_by_focus_date", new Dictionary<string, object?> { ["calendarPeriod"] = "today" }, ct));
		second.GetProperty("calendar").GetProperty("today").GetString().Should().Be("2026-08-20");
		second.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(taskA);
		second.ToString().Should().NotContain("B private task");
		var other = ParseJsonElement(await b.CallToolAsync("taskitem_list_by_focus_date", new Dictionary<string, object?> { ["calendarPeriod"] = "today" }, ct));
		other.GetProperty("calendar").GetProperty("today").GetString().Should().Be("2026-08-19");
		other.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().Should().Be(taskB);
		foreach (var (adapter, zone, start, ownTitle, foreignTitle) in new[]
		{
			(a, "Asia/Tokyo", "2026-08-17", "A local task", "B private task"),
			(b, "America/Los_Angeles", "2026-08-16", "B private task", "A local task")
		})
		{
			var period = ParseJsonElement(await adapter.CallToolAsync("calendar_period_get", new Dictionary<string, object?> { ["period"] = "thisWeek" }, ct));
			period.GetProperty("timeZone").GetString().Should().Be(zone);
			period.GetProperty("startDate").GetString().Should().Be(start);
			foreach (var tool in new[] { "general_report_get", "weekly_report_get", "reflection_report_get" })
			{
				var report = ParseJsonElement(await adapter.CallToolAsync(tool, new Dictionary<string, object?> { ["calendarPeriod"] = "thisWeek" }, ct));
				report.ToString().Should().Contain(ownTitle).And.NotContain(foreignTitle);
				report.GetProperty("calendar").ToString().Should().Contain(zone).And.Contain(start);
			}
		}
		// A local request leaves every old page field intact; omitting it preserves the old result shape.
		var legacy = ParseJsonElement(await a.CallToolAsync("taskitem_list_by_focus_date", new Dictionary<string, object?> { ["focusDate"] = "2026-08-20" }, ct));
		legacy.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("items", "totalCount", "offset", "limit", "hasMore");
		foreach (var user in new[] { "calendar-a", "calendar-b" })
		{
			await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
				.UseSqlite($"Data Source={Path.Combine(_tempDirectory, $"ownplanner-user-{user}.db")}").Options);
			(await db.WeeklyReviews.CountAsync(ct)).Should().Be(0);
			(await db.WeeklyReviewPreferences.SingleAsync(ct)).Enabled.Should().BeFalse();
		}
	}

	[Fact]
	public async Task DirectSchemasExposeOnlyAdditiveCalendarOptionsAndInvalidPeriodsReturnErrors()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		await SeedUserTaskIdAsync("calendar-schema", "Task", ct);
		await using var adapter = CreateAdapter(services, "calendar-schema");
		var details = await adapter.ListToolDetailsAsync(ct);
		foreach (var name in new[] { "general_report_get", "weekly_report_get", "reflection_report_get", "taskitem_list_by_focus_date" })
		{
			var schema = details.Single(t => t.Name == name).JsonSchema!.Value;
			schema.GetProperty("properties").GetProperty("calendarPeriod").GetProperty("type").GetString().Should().Be("string");
			schema.GetProperty("properties").TryGetProperty("userId", out _).Should().BeFalse();
			if (schema.TryGetProperty("required", out var required)) required.EnumerateArray().Should().BeEmpty();
			var error = ParseJsonElement(await adapter.CallToolAsync(name, new Dictionary<string, object?> { ["calendarPeriod"] = "invalid" }, ct));
			error.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
		}
		var fallback = ParseJsonElement(await adapter.CallToolAsync("calendar_period_get", null, ct));
		fallback.GetProperty("fallbackExplanation").GetString().Should().Contain("UTC");
		fallback.GetProperty("timeZone").GetString().Should().Be("UTC");
	}

	private sealed class CalendarClock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
}
