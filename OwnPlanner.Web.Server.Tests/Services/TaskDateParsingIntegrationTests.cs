using System.Globalization;
using System.Text.Json;
using FluentAssertions;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Theory]
	[InlineData("2026-08-19T12:00:00Z", "2026-08-19T12:00:00Z", "2026-08-19T12:00:00Z")]
	[InlineData("2026-08-19T00:30:00+02:00", "2026-08-18T22:30:00Z", "2026-08-19T00:30:00Z")]
	[InlineData("2026-08-19T23:30:00-07:00", "2026-08-20T06:30:00Z", "2026-08-19T23:30:00Z")]
	[InlineData("2026-08-19T23:30:00Z", "2026-08-19T23:30:00Z", "2026-08-19T23:30:00Z")]
	[InlineData("2026-08-19", "2026-08-19T00:00:00Z", "2026-08-19T00:00:00Z")]
	[InlineData("2026-08-19T12:00:00", "2026-08-19T12:00:00Z", "2026-08-19T12:00:00Z")]
	public async Task TaskDates_PersistDeadlineInstantAndSelectedFocusDate(string input, string dueUtc, string focusUtc)
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var listId = await SeedUserTaskAsync("user-a", "Unfocused seed", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		var expectedDue = DateTime.Parse(dueUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
		var expectedFocus = DateTime.Parse(focusUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

		var created = ParseJsonElement(await adapter.CallToolAsync("taskitem_create", new Dictionary<string, object?>
		{
			["title"] = "Calendar regression", ["taskListId"] = listId, ["dueAt"] = input
		}, ct));
		var id = created.GetProperty("id").GetGuid();
		created.GetProperty("dueAt").GetDateTime().Should().Be(expectedDue);
		await adapter.CallToolAsync("taskitem_set_focus_date", new Dictionary<string, object?>
		{
			["id"] = id, ["focusDate"] = input
		}, ct);

		// Fresh adapters resolve separate tenant-bound contexts and read the actual persisted values.
		await using var fresh = CreateAdapter(services, "user-a");
		var persisted = ParseJsonElement(await fresh.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
		persisted.GetProperty("dueAt").GetDateTime().Should().Be(expectedDue);
		persisted.GetProperty("focusAt").GetDateTime().Should().Be(expectedFocus);
		foreach (var query in new[] { input, expectedFocus.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) })
		{
			var focused = ParseJsonElement(await fresh.CallToolAsync("taskitem_list_by_focus_date",
				new Dictionary<string, object?> { ["focusDate"] = query }, ct));
			focused.GetProperty("items").EnumerateArray().Should().ContainSingle()
				.Which.GetProperty("id").GetGuid().Should().Be(id);
		}

		await fresh.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["clearDueAt"] = true }, ct);
		var cleared = ParseJsonElement(await fresh.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
		cleared.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		cleared.GetProperty("focusAt").GetDateTime().Should().Be(expectedFocus);

		await fresh.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = input }, ct);
		var updated = ParseJsonElement(await fresh.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
		updated.GetProperty("dueAt").GetDateTime().Should().Be(expectedDue);
		updated.GetProperty("focusAt").GetDateTime().Should().Be(expectedFocus);
	}
}
