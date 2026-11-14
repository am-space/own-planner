using System.Text.Json;
using FluentAssertions;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Fact]
	public async Task DirectConfigurationAndClosedHistoryCorrectionUseSharedClockAndKeepOriginalParentClosed()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var id = await SeedUserTaskIdAsync("own", "Existing ordinary work", ct);
		await using var direct = CreateAdapter(services, "own");
		async Task<JsonElement> Call(string name, Dictionary<string, object?> args)
		{
			using var doc = JsonDocument.Parse(await direct.CallToolAsync(name, args, ct));
			return doc.RootElement.Clone();
		}
		var configuration = new Dictionary<string, object?> { ["taskId"] = id, ["startDate"] = "2026-06-01", ["endDate"] = "2026-07-31",
			["frequency"] = "Monthly", ["monthDays"] = new[] { 1 }, ["missedPolicy"] = "SkipExpiredPeriods" };
		var configured = await Call("recurring_task_configure", configuration);
		configured.GetProperty("task").GetProperty("id").GetGuid().Should().Be(id);
		(await Call("recurring_task_configure", configuration)).TryGetProperty("error", out _).Should().BeTrue();
		// Read-only projection can describe effective skips without persisting terminal facts.
		var range = new Dictionary<string, object?> { ["taskId"] = id, ["basis"] = "ScheduledDate", ["startDate"] = "2026-06-01", ["endExclusiveDate"] = "2026-08-01" };
		(await Call("task_occurrence_history", range)).GetProperty("totalCount").GetInt32().Should().Be(0);
		var invalid = await Call("task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = "2026-07-01", ["completedAt"] = "2026-08-20T09:00:00Z" });
		invalid.TryGetProperty("error", out _).Should().BeTrue();
		(await Call("task_occurrence_history", range)).GetProperty("totalCount").GetInt32().Should().Be(0);
		await Call("recurring_task_end", new() { ["taskId"] = id });
		var before = await Call("task_occurrence_get", new() { ["taskId"] = id, ["occurrenceDate"] = "2026-07-01" });
		before.GetProperty("status").GetString().Should().Be("Skipped");
		var done = await Call("task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = "2026-07-01", ["completedAt"] = "2026-08-18T09:00:00-04:00" });
		done.GetProperty("id").GetProperty("occurrenceDate").GetString().Should().Be("2026-07-01");
		done.GetProperty("completedAt").GetDateTime().Should().Be(TenantTestUtcNow.AddDays(-1).AddHours(1));
		var retry = await Call("task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = "2026-07-01" });
		JsonElement.DeepEquals(done, retry).Should().BeTrue();
		var parent = await Call("taskitem_get", new() { ["id"] = id });
		parent.GetProperty("isCompleted").GetBoolean().Should().BeTrue();
		parent.TryGetProperty("completedAt", out _).Should().BeFalse();
		(await Call("task_occurrence_history", range)).GetProperty("totalCount").GetInt32().Should().Be(2);
	}
}
