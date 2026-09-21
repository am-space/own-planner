using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed partial class DirectToolMcpAdapterTests
{
	[Theory]
	[InlineData(-1, null)]
	[InlineData(0, "2026-09-21")]
	[InlineData(1, "invalid")]
	public async Task ClearDeadline_PersistsAndRefreshesCommitmentsWithoutChangingFocus(int dueDay, string? suppliedDate)
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var id = await SeedUserTaskIdAsync("user-a", "Flexible task", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		var args = new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = TenantTestUtcNow.AddDays(dueDay).ToString("O") };
		await adapter.CallToolAsync("taskitem_update", args, ct);
		await adapter.CallToolAsync("taskitem_set_focus_date", new Dictionary<string, object?> { ["id"] = id, ["focusDate"] = TenantTestUtcNow.ToString("O") }, ct);
		var before = ParseJsonElement(await adapter.CallToolAsync("general_report_get", cancellationToken: ct));
		before.GetProperty("commitments").GetProperty("nearest").GetProperty("totalCount").GetInt32().Should().Be(1);

		args["dueAt"] = suppliedDate;
		args["clearDueAt"] = true;
		var updated = ParseJsonElement(await adapter.CallToolAsync("taskitem_update", args, ct));
		updated.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		// A fresh adapter/read uses a separate context against the same tenant database.
		await using var fresh = CreateAdapter(services, "user-a");
		var persisted = ParseJsonElement(await fresh.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
		persisted.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		persisted.GetProperty("focusAt").GetDateTime().Should().Be(TenantTestUtcNow);
		var report = ParseJsonElement(await fresh.CallToolAsync("general_report_get", cancellationToken: ct));
		foreach (var bucket in new[] { "overdueCount", "dueTodayCount", "upcomingSevenDayCount" })
			report.GetProperty("commitments").GetProperty(bucket).GetInt32().Should().Be(0);
		report.GetProperty("commitments").GetProperty("nearest").GetProperty("totalCount").GetInt32().Should().Be(0);
		report.GetProperty("today").GetProperty("focusTaskCount").GetInt32().Should().Be(1);
		var focused = ParseJsonElement(await fresh.CallToolAsync("taskitem_list_by_focus_date", new Dictionary<string, object?> { ["focusDate"] = TenantTestUtcNow.ToString("O") }, ct));
		focused.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(id);
	}

	[Fact]
	public async Task ClearDeadline_RespectsTenantAndDelegationScopeAndProposalRestrictions()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var own = await SeedUserTaskIdAsync("user-a", "Own", ct);
		var outside = await SeedUserTaskIdAsync("user-a", "Outside list", ct);
		var other = await SeedUserTaskIdAsync("user-b", "Private", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		await using var otherAdapter = CreateAdapter(services, "user-b");
		var originalDate = TenantTestUtcNow.ToString("O");
		foreach (var id in new[] { own, outside })
			await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = originalDate }, ct);
		await otherAdapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = other, ["dueAt"] = originalDate }, ct);
		var task = ParseJsonElement(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = own }, ct));
		var list = task.GetProperty("taskListId").GetGuid();
		var execution = await TaskPlanningMcpAdapter.CreateAsync(adapter, new TaskPlanningAgentRequest("Remove deadline", TaskListId: list), ct);
		var proposal = await TaskPlanningMcpAdapter.CreateAsync(adapter, new TaskPlanningAgentRequest("Consider removal", TaskListId: list, Behavior: TaskPlanningBehavior.Proposal), ct);
		var args = new Dictionary<string, object?> { ["id"] = own, ["clearDueAt"] = true };
		var deniedProposal = () => proposal.CallToolAsync("taskitem_update", args, ct);
		await deniedProposal.Should().ThrowAsync<InvalidOperationException>();
		args["id"] = outside;
		var deniedScope = () => execution.CallToolAsync("taskitem_update", args, ct);
		await deniedScope.Should().ThrowAsync<InvalidOperationException>();
		args["id"] = other;
		(await adapter.CallToolAsync("taskitem_update", args, ct)).Should().Contain("not found");
		var deniedTenant = () => execution.CallToolAsync("taskitem_update", args, ct);
		await deniedTenant.Should().ThrowAsync<InvalidOperationException>();
		foreach (var id in new[] { own, outside, other })
		{
			var host = id == other ? otherAdapter : adapter;
			var unchanged = ParseJsonElement(await host.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
			unchanged.GetProperty("dueAt").GetDateTime().Should().Be(TenantTestUtcNow);
		}
		args["id"] = own;
		ParseJsonElement(await execution.CallToolAsync("taskitem_update", args, ct)).GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		execution.Actions.Should().ContainSingle();
		proposal.Actions.Should().BeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task General_ScriptedDeadlineRemoval_ReportsOnlyConfirmedResult(bool missingTask)
	{
		var ct = TestContext.Current.CancellationToken;
		await using var services = BuildTenantServiceProvider();
		var id = await SeedUserTaskIdAsync("user-a", "Flexible task", ct);
		await using var adapter = CreateAdapter(services, "user-a");
		await adapter.CallToolAsync("taskitem_update", new Dictionary<string, object?> { ["id"] = id, ["dueAt"] = TenantTestUtcNow.ToString("O") }, ct);
		using var provider = new DeadlineProvider(missingTask ? Guid.NewGuid() : id);
		await using var chat = new ChatServiceAdapter("AIza" + new string('x', 35), "test-model", mcpAdapter: adapter, httpClientFactory: provider);
		await using var planner = new PlanningService(chat, null, services.GetRequiredService<ILogger<PlanningService>>());
		await planner.SwitchModeAsync(PlanningMode.General, ct);

		await planner.GetResponseAsync("Remove the deadline from the identified task", ct);

		provider.Confirmed.Should().Be(!missingTask);
		provider.Instructions.Should().Contain("clearDueAt=true");
		var persisted = ParseJsonElement(await adapter.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = id }, ct));
		persisted.GetProperty("dueAt").ValueKind.Should().Be(missingTask ? JsonValueKind.String : JsonValueKind.Null);
	}

	private sealed class DeadlineProvider(Guid id) : HttpMessageHandler, IHttpClientFactory
	{
		private int _round;
		public bool Confirmed { get; private set; }
		public string Instructions { get; private set; } = "";
		public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = ParseJsonElement(await request.Content!.ReadAsStringAsync(cancellationToken));
			object[] parts;
			if (_round == 0)
				parts = [new { functionCall = new { name = "skill_load", args = new { skillId = "task_management" } } }];
			else if (_round == 1)
			{
				Instructions = body.GetProperty("systemInstruction").ToString();
				var declaration = body.GetProperty("tools").EnumerateArray()
					.Where(x => x.TryGetProperty("functionDeclarations", out _))
					.SelectMany(x => x.GetProperty("functionDeclarations").EnumerateArray())
					.Single(x => x.GetProperty("name").GetString() == "taskitem_update");
				declaration.ToString().Should().Contain("clearDueAt");
				parts = [new { functionCall = new { name = "taskitem_update", args = new { id, clearDueAt = true } } }];
			}
			else
			{
				var response = body.GetProperty("contents").EnumerateArray()
					.SelectMany(x => x.GetProperty("parts").EnumerateArray())
					.Where(x => x.TryGetProperty("functionResponse", out _))
					.Select(x => x.GetProperty("functionResponse"))
					.Last(x => x.GetProperty("name").GetString() == "taskitem_update").GetProperty("response");
				// The scripted model emits success only after receiving the real persisted tool result.
				var result = ParseJsonElement(response.GetProperty("result").GetString()!);
				Confirmed = result.TryGetProperty("dueAt", out var dueAt) && dueAt.ValueKind == JsonValueKind.Null
					&& result.GetProperty("id").GetGuid() == id;
				parts = [new { text = Confirmed ? "Deadline removed." : "The task could not be updated." }];
			}
			_round++;
			var json = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } } });
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
		}
	}
}
