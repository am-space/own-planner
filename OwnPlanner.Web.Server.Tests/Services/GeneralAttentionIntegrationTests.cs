using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task AttentionQueryAndPreloadAreSharedTenantBoundAndRefreshAfterCompletion()
	{
		var ct = TestContext.Current.CancellationToken;
		var clock = new CalendarClock(new DateTime(2026, 8, 19, 16, 0, 0, DateTimeKind.Utc));
		await using var services = BuildTenantServiceProvider(s => s.AddSingleton<TimeProvider>(clock));
		var taskA = await SeedUserTaskIdAsync("attention-a", "A attention", ct);
		var taskB = await SeedUserTaskIdAsync("attention-b", "B private", ct);
		await using var a = CreateAdapter(services, "attention-a");
		await using var b = CreateAdapter(services, "attention-b");
		foreach (var (adapter, zone, start, task, date) in new[] {
			(a, "Asia/Tokyo", 1, taskA, "2026-08-20"), (b, "America/Los_Angeles", 0, taskB, "2026-08-19") })
		{
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?>
				{ ["enabled"] = false, ["timeZoneId"] = zone, ["weekStart"] = start }, ct);
			await adapter.CallToolAsync("taskitem_set_focus_date", new Dictionary<string, object?> { ["id"] = task, ["focusDate"] = date }, ct);
		}
		foreach (var (adapter, own, foreign, zone, firstDate) in new[] {
			(a, "A attention", "B private", "Asia/Tokyo", "2026-08-17"),
			(b, "B private", "A attention", "America/Los_Angeles", "2026-08-16") })
		{
			var overview = ParseJsonElement(await adapter.CallToolAsync("general_attention_get", cancellationToken: ct));
			overview.GetProperty("tasks").EnumerateArray().Should().ContainSingle();
			overview.ToString().Should().Contain(own).And.NotContain(foreign);
			overview.GetProperty("period").GetProperty("timeZone").GetString().Should().Be(zone);
			overview.GetProperty("period").GetProperty("startDate").GetString().Should().Be(firstDate);
			var preload = ParseJsonElement(await adapter.CallToolAsync("general_report_get", new Dictionary<string, object?> { ["calendarPeriod"] = "thisWeek" }, ct));
			preload.GetProperty("calendar").GetProperty("attention").ToString().Should().Be(overview.ToString());
		}
		var schema = (await a.ListToolDetailsAsync(ct)).Single(t => t.Name == "general_attention_get").JsonSchema!.Value;
		schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("section", "offset", "limit");
		if (schema.TryGetProperty("required", out var required)) required.EnumerateArray().Should().BeEmpty();
		var invalid = ParseJsonElement(await a.CallToolAsync("general_attention_get", new Dictionary<string, object?> { ["section"] = "invalid" }, ct));
		invalid.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
		var page = ParseJsonElement(await a.CallToolAsync("general_attention_get", new Dictionary<string, object?> { ["section"] = "today", ["offset"] = 1, ["limit"] = 1 }, ct));
		page.GetProperty("tasks").GetArrayLength().Should().Be(0);
		page.GetProperty("sections")[0].GetProperty("totalCount").GetInt32().Should().Be(1);
		await a.CallToolAsync("taskitem_complete", new Dictionary<string, object?> { ["id"] = taskA }, ct);
		var fresh = ParseJsonElement(await a.CallToolAsync("general_attention_get", cancellationToken: ct));
		fresh.GetProperty("tasks").GetArrayLength().Should().Be(0);
		var other = ParseJsonElement(await b.CallToolAsync("general_attention_get", cancellationToken: ct));
		other.GetProperty("tasks").EnumerateArray().Should().ContainSingle();
	}
}
