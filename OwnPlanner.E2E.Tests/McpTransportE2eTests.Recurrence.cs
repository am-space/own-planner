using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OwnPlanner.Domain;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.E2E.Tests;

public sealed partial class McpTransportE2eTests
{
	[Fact]
	public async Task HttpAndDirect_RecurrenceFlowAndEveryToolRespectBearerTenantIsolation()
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
		using var userA = CreateHttpClient(); using var userB = CreateHttpClient();
		var (idA, tokenA, _) = await RegisterWithTokenAsync(userA, ct);
		var (_, tokenB, _) = await RegisterWithTokenAsync(userB, ct);
		await using var clientA = await McpClient.CreateAsync(CreateHttpTransport(tokenA), cancellationToken: ct);
		await using var clientB = await McpClient.CreateAsync(CreateHttpTransport(tokenB), cancellationToken: ct);
		var source = await ExerciseRecurrenceAsync(clientA, "KeepStartAsSeparateWork", ct);
		var id = source.GetProperty("task").GetProperty("id").GetGuid();
		await using var direct = ActivatorUtilities.CreateInstance<DirectToolMcpAdapter>(application.Services, Guid.NewGuid().ToString(), idA.ToString());
		using var directRead = JsonDocument.Parse(await direct.CallToolAsync("recurring_task_get", new Dictionary<string, object?> { ["taskId"] = id }, ct));
		var httpRead = await CallAsync(clientA, "recurring_task_get", new() { ["taskId"] = id }, ct);
		JsonElement.DeepEquals(httpRead.GetProperty("schedule"), directRead.RootElement.GetProperty("schedule")).Should().BeTrue();
		var start = source.GetProperty("schedule").GetProperty("startDate").GetString()!;
		var facts = await CallAsync(clientA, "task_occurrence_history", new() { ["basis"] = "ScheduledDate", ["startDate"] = start,
			["endExclusiveDate"] = DateOnly.Parse(start).AddDays(14).ToString("yyyy-MM-dd"), ["taskId"] = id }, ct);
		using var directFacts = JsonDocument.Parse(await direct.CallToolAsync("task_occurrence_history", new Dictionary<string, object?>
			{ ["basis"] = "ScheduledDate", ["startDate"] = start, ["endExclusiveDate"] = DateOnly.Parse(start).AddDays(14).ToString("yyyy-MM-dd"), ["taskId"] = id }, ct));
		JsonElement.DeepEquals(facts.GetProperty("items"), directFacts.RootElement.GetProperty("items")).Should().BeTrue("DateOnly, status and UTC fact serialization agree across transports");
		await AssertForeignRecurrenceDenied(clientB, id, source.GetProperty("task").GetProperty("taskListId").GetGuid(), start, ct);
		var directDefinitions = await direct.ListToolDetailsAsync(ct);
		foreach (var tool in (await clientA.ListToolsAsync(cancellationToken: ct)).Where(t => IsRecurrenceTool(t.Name)))
		{
			var directSchema = directDefinitions.Single(d => d.Name == tool.Name).JsonSchema!.Value;
			JsonElement.DeepEquals(directSchema, tool.JsonSchema).Should().BeTrue($"{tool.Name} has the exact same SDK-generated argument schema");
			directSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should()
				.BeEquivalentTo(tool.JsonSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
			directSchema.GetProperty("properties").EnumerateObject().Select(p => (p.Name, Type: p.Value.GetProperty("type").ToString())).Should()
				.BeEquivalentTo(tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => (p.Name, Type: p.Value.GetProperty("type").ToString())));
		}
	}

	[Fact]
	public async Task Stdio_RecurrenceFlowPreservesHistoryAcrossRestartAndUserFiles()
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(90)); var ct = timeout.Token;
		var root = Path.Combine(Path.GetTempPath(), "ownplanner-recurrence-mcp", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			JsonElement source;
			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "a"), cancellationToken: ct))
				source = await ExerciseRecurrenceAsync(client, "MoveStartAfterFacts", ct);
			var id = source.GetProperty("task").GetProperty("id").GetGuid();
			var date = source.GetProperty("schedule").GetProperty("startDate").GetString()!;
			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "a"), cancellationToken: ct))
			{
				var recorded = await CallAsync(client, "task_occurrence_get", new() { ["taskId"] = id, ["occurrenceDate"] = date }, ct);
				recorded.GetProperty("id").GetProperty("occurrenceDate").GetString().Should().Be(date);
				recorded.GetProperty("completedAt").GetString().Should().EndWith("Z");
			}
			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "b"), cancellationToken: ct))
				await AssertForeignRecurrenceDenied(client, id, source.GetProperty("task").GetProperty("taskListId").GetGuid(), date, ct);
		}
		finally { Directory.Delete(root, true); }
	}

	private static async Task<JsonElement> ExerciseRecurrenceAsync(McpClient client, string choice, CancellationToken ct)
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow); var next = today.AddDays(7);
		string D(DateOnly date) => date.ToString("yyyy-MM-dd");
		var context = await CallAsync(client, "context_create", new() { ["name"] = "Recurrence transport", ["type"] = "Area" }, ct);
		var list = await CallAsync(client, "tasklist_create", new() { ["contextId"] = context.GetProperty("id").GetGuid(), ["title"] = "Recurrence transport" }, ct);
		var create = new Dictionary<string, object?> { ["title"] = "Transport recurrence — 日本語", ["taskListId"] = list.GetProperty("id").GetGuid(),
			["startDate"] = D(today), ["frequency"] = "Weekly", ["weekdays"] = new[] { today.DayOfWeek.ToString() }, ["missedPolicy"] = "RetainEachObligation" };
		var source = await CallAsync(client, "recurring_task_create", create, ct);
		var id = source.GetProperty("task").GetProperty("id").GetGuid();
		var query = new Dictionary<string, object?> { ["startDate"] = D(today), ["endExclusiveDate"] = D(today.AddDays(14)), ["taskId"] = id, ["limit"] = 1 };
		var first = await CallAsync(client, "planning_work_query", query, ct);
		first.GetProperty("totalCount").GetInt32().Should().Be(2); first.GetProperty("hasMore").GetBoolean().Should().BeTrue();
		first.GetProperty("items")[0].GetProperty("occurrenceDate").GetString().Should().Be(D(today));
		query["offset"] = 1; var second = await CallAsync(client, "planning_work_query", query, ct);
		second.GetProperty("items")[0].GetProperty("taskId").GetGuid().Should().Be(id);
		second.GetProperty("items")[0].GetProperty("occurrenceDate").GetString().Should().Be(D(next));
		var actual = DateTime.UtcNow.AddHours(-2).ToString("O");
		var complete = new Dictionary<string, object?> { ["taskId"] = id, ["occurrenceDate"] = D(today), ["completedAt"] = actual };
		var done = await CallAsync(client, "task_occurrence_complete", complete, ct);
		complete.Remove("completedAt"); var retry = await CallAsync(client, "task_occurrence_complete", complete, ct);
		JsonElement.DeepEquals(done, retry).Should().BeTrue();
		done.GetProperty("completedAt").GetDateTime().Should().Be(DateTime.Parse(actual).ToUniversalTime());
		query.Remove("offset"); var refreshed = await CallAsync(client, "planning_work_query", query, ct);
		refreshed.GetProperty("totalCount").GetInt32().Should().Be(1);
		var parent = await CallAsync(client, "taskitem_get", new() { ["id"] = id }, ct);
		parent.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
		var cancelled = await CallAsync(client, "task_occurrence_cancel", new() { ["taskId"] = id, ["occurrenceDate"] = D(next) }, ct);
		cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
		cancelled.TryGetProperty("completedAt", out _).Should().BeFalse();
		await CallAsync(client, "task_occurrence_complete", new() { ["taskId"] = id, ["occurrenceDate"] = D(today.AddDays(14)) }, ct);
		var previewArgs = new Dictionary<string, object?> { ["taskId"] = id, ["kind"] = "Replace", ["startDate"] = D(next), ["frequency"] = "Monthly", ["monthDays"] = new[] { 31 } };
		var preview = await CallAsync(client, "recurring_task_successor_preview", previewArgs, ct);
		preview.GetProperty("affectedFacts").GetArrayLength().Should().Be(2);
		var apply = new Dictionary<string, object?>(previewArgs) { ["requestId"] = Guid.NewGuid(), ["previewToken"] = preview.GetProperty("previewToken").GetString() };
		(await CallAsync(client, "recurring_task_successor_apply", apply, ct)).TryGetProperty("error", out _).Should().BeTrue("a token alone cannot choose how to resolve conflicting future work");
		await CallAsync(client, "taskitem_update", new() { ["id"] = id, ["title"] = "Changed since preview" }, ct);
		apply["resolution"] = choice;
		(await CallAsync(client, "recurring_task_successor_apply", apply, ct)).TryGetProperty("error", out _).Should().BeTrue("stale decisions cannot mutate");
		preview = await CallAsync(client, "recurring_task_successor_preview", previewArgs, ct);
		apply["previewToken"] = preview.GetProperty("previewToken").GetString();
		var successor = await CallAsync(client, "recurring_task_successor_apply", apply, ct);
		var again = await CallAsync(client, "recurring_task_successor_apply", apply, ct); JsonElement.DeepEquals(successor, again).Should().BeTrue();
		var successorId = successor.GetProperty("successorTaskId").GetGuid();
		var oldFact = await CallAsync(client, "task_occurrence_get", new() { ["taskId"] = id, ["occurrenceDate"] = D(next) }, ct);
		oldFact.GetProperty("id").GetProperty("taskId").GetGuid().Should().Be(id);
		var ending = await CallAsync(client, "recurring_task_end", new() { ["taskId"] = successorId }, ct);
		ending.GetProperty("seriesEnded").GetBoolean().Should().BeTrue();
		(await CallAsync(client, "taskitem_reopen", new() { ["id"] = successorId }, ct)).TryGetProperty("error", out _).Should().BeTrue();
		var resume = new Dictionary<string, object?> { ["taskId"] = successorId, ["kind"] = "Resume", ["startDate"] = D(today.AddDays(28)) };
		var resumePreview = await CallAsync(client, "recurring_task_successor_preview", resume, ct);
		resume["requestId"] = Guid.NewGuid(); resume["previewToken"] = resumePreview.GetProperty("previewToken").GetString();
		var resumed = await CallAsync(client, "recurring_task_successor_apply", resume, ct);
		resumed.GetProperty("successorTaskId").GetGuid().Should().NotBe(successorId);
		parent = await CallAsync(client, "taskitem_get", new() { ["id"] = successorId }, ct);
		parent.GetProperty("isCompleted").GetBoolean().Should().BeTrue();
		return source;
	}

	private static async Task AssertForeignRecurrenceDenied(McpClient client, Guid id, Guid foreignList, string date, CancellationToken ct)
	{
		var actions = new Dictionary<string, Dictionary<string, object?>>
		{
			["recurring_task_get"] = new() { ["taskId"] = id }, ["recurring_task_end"] = new() { ["taskId"] = id },
			["task_occurrence_get"] = new() { ["taskId"] = id, ["occurrenceDate"] = date },
			["task_occurrence_complete"] = new() { ["taskId"] = id, ["occurrenceDate"] = date },
			["task_occurrence_cancel"] = new() { ["taskId"] = id, ["occurrenceDate"] = date },
			["recurring_task_configure"] = new() { ["taskId"] = id, ["startDate"] = date, ["frequency"] = "Monthly", ["monthDays"] = new[] { 1 }, ["missedPolicy"] = "RetainEachObligation" },
			["recurring_task_create"] = new() { ["title"] = "Foreign write", ["taskListId"] = foreignList, ["startDate"] = date, ["frequency"] = "Monthly", ["monthDays"] = new[] { 1 }, ["missedPolicy"] = "RetainEachObligation" },
			["recurring_task_successor_preview"] = new() { ["taskId"] = id, ["kind"] = "Replace", ["startDate"] = date },
			["recurring_task_successor_apply"] = new() { ["taskId"] = id, ["kind"] = "Replace", ["startDate"] = date, ["requestId"] = Guid.NewGuid(), ["previewToken"] = "foreign" }
		};
		foreach (var (tool, args) in actions) (await CallAsync(client, tool, args, ct)).TryGetProperty("error", out _).Should().BeTrue(tool);
		var end = DateOnly.Parse(date).AddDays(14).ToString("yyyy-MM-dd");
		(await CallAsync(client, "planning_work_query", new() { ["taskId"] = id, ["startDate"] = date, ["endExclusiveDate"] = end }, ct)).GetProperty("totalCount").GetInt32().Should().Be(0);
		(await CallAsync(client, "task_occurrence_history", new() { ["taskId"] = id, ["basis"] = "ScheduledDate", ["startDate"] = date, ["endExclusiveDate"] = end }, ct)).GetProperty("totalCount").GetInt32().Should().Be(0);
	}

	private static bool IsRecurrenceTool(string name) => name.StartsWith("recurring_task_", StringComparison.Ordinal)
		|| name.StartsWith("task_occurrence_", StringComparison.Ordinal) || name == "planning_work_query";
}
