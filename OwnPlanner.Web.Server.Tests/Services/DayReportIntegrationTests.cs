using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task DailyDeadlinesAreSharedTenantBoundAndRefreshAfterCompletion()
	{
		var ct = TestContext.Current.CancellationToken;
		var clock = new CalendarClock(new DateTime(2026, 8, 19, 16, 0, 0, DateTimeKind.Utc));
		await using var services = BuildTenantServiceProvider(s => s.AddSingleton<TimeProvider>(clock));
		var taskA = await SeedUserTaskIdAsync("attention-a", "A attention", ct);
		var taskB = await SeedUserTaskIdAsync("attention-b", "B private", ct);
		await using var a = CreateAdapter(services, "attention-a");
		await using var b = CreateAdapter(services, "attention-b");
		foreach (var (adapter, zone, start, task) in new[] {
			(a, "Asia/Tokyo", 1, taskA), (b, "America/Los_Angeles", 0, taskB) })
		{
			await adapter.CallToolAsync("weekly_review_configure", new Dictionary<string, object?>
				{ ["enabled"] = false, ["timeZoneId"] = zone, ["weekStart"] = start }, ct);
			await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = task, ["dueAt"] = "2026-08-19T17:00:00Z" }, ct);
			await adapter.CallToolAsync("taskitem_set_important", new Dictionary<string, object?> { ["id"] = task, ["isImportant"] = true }, ct);
		}
		foreach (var (adapter, own, foreign, zone) in new[] {
			(a, "A attention", "B private", "Asia/Tokyo"),
			(b, "B private", "A attention", "America/Los_Angeles") })
		{
			var overview = ParseJsonElement(await adapter.CallToolAsync("day_report_get", cancellationToken: ct));
			overview.GetProperty("tasks").EnumerateArray().Should().ContainSingle();
			overview.GetProperty("tasks")[0].GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).Should().Equal("dueToday");
			overview.GetProperty("tasks")[0].GetProperty("isImportant").GetBoolean().Should().BeTrue();
			overview.GetProperty("tasks")[0].GetProperty("dueAt").GetString().Should().Be("2026-08-19T17:00:00Z");
			overview.ToString().Should().Contain(own).And.NotContain(foreign);
			overview.GetProperty("period").GetProperty("timeZone").GetString().Should().Be(zone);
			overview.GetProperty("period").GetProperty("today").GetString().Should().Be(zone == "Asia/Tokyo" ? "2026-08-20" : "2026-08-19");
		}
		var schema = (await a.ListToolDetailsAsync(ct)).Single(t => t.Name == "day_report_get").JsonSchema!.Value;
		schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("section", "offset", "limit");
		if (schema.TryGetProperty("required", out var required)) required.EnumerateArray().Should().BeEmpty();
		var invalid = ParseJsonElement(await a.CallToolAsync("day_report_get", new Dictionary<string, object?> { ["section"] = "invalid" }, ct));
		invalid.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
		var page = ParseJsonElement(await a.CallToolAsync("day_report_get", new Dictionary<string, object?> { ["section"] = "today", ["offset"] = 1, ["limit"] = 1 }, ct));
		page.GetProperty("tasks").GetArrayLength().Should().Be(0);
		page.GetProperty("sections")[0].GetProperty("totalCount").GetInt32().Should().Be(1);
		await a.CallToolAsync("taskitem_complete", new Dictionary<string, object?> { ["id"] = taskA }, ct);
		var fresh = ParseJsonElement(await a.CallToolAsync("day_report_get", cancellationToken: ct));
		fresh.GetProperty("tasks").GetArrayLength().Should().Be(0);
		var other = ParseJsonElement(await b.CallToolAsync("day_report_get", cancellationToken: ct));
		other.GetProperty("tasks").EnumerateArray().Should().ContainSingle();
	}
}
