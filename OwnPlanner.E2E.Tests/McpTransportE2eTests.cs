using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class McpTransportE2eTests(E2eWebApplicationFactory application)
{
	[Fact]
	public async Task Http_DiscoveryAndCalls_PreserveContractsAndBearerTenantIsolation()
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(60));
		var ct = timeout.Token;
		using var userA = CreateHttpClient();
		using var userB = CreateHttpClient();
		var (idA, tokenA, tokenIdA) = await RegisterWithTokenAsync(userA, ct);
		var (_, tokenB, _) = await RegisterWithTokenAsync(userB, ct);
		using var anonymous = CreateHttpClient();
		await AssertUnauthorizedAsync(anonymous, null, ct);

		// A valid web cookie is deliberately insufficient for the bearer-only MCP endpoint.
		await AssertUnauthorizedAsync(userA, null, ct);
		await AssertUnauthorizedAsync(userA, "invalid-test-token", ct);

		await using var transportA = CreateHttpTransport(tokenA);
		await using var clientA = await McpClient.CreateAsync(transportA, cancellationToken: ct);
		await using var transportB = CreateHttpTransport(tokenB);
		await using var clientB = await McpClient.CreateAsync(transportB, cancellationToken: ct);
		clientA.SessionId.Should().BeNull("the HTTP server explicitly uses stateless requests");
		await AssertDiscoveryAsync(clientA, ct);
		var task = await ExerciseTaskContractAsync(clientA, ct);
		var taskId = task.GetProperty("id").GetGuid();

		var hidden = await CallAsync(clientB, "taskitem_get", new() { ["id"] = taskId }, ct);
		hidden.GetProperty("error").GetString().Should().Be("Task not found");
		var otherTasks = await CallAsync(clientB, "taskitem_list_items", new(), ct);
		otherTasks.GetProperty("totalCount").GetInt32().Should().Be(0);
		var deniedUpdate = await CallAsync(clientB, "taskitem_update", new() { ["id"] = taskId, ["title"] = "Cross-user overwrite" }, ct);
		deniedUpdate.GetProperty("error").GetString().Should().Contain("not found");

		// Read the HTTP-created task through the actual in-process chat adapter and compare results.
		await using var direct = ActivatorUtilities.CreateInstance<DirectToolMcpAdapter>(
			application.Services, Guid.NewGuid().ToString(), idA.ToString());
		using var directResult = JsonDocument.Parse(await direct.CallToolAsync("taskitem_get", new Dictionary<string, object?> { ["id"] = taskId }, ct));
		AssertTaskContent(task, directResult.RootElement);
		var directTools = await direct.ListToolDetailsAsync(ct);
		var httpTools = await clientA.ListToolsAsync(cancellationToken: ct);
		httpTools.Select(x => x.Name).Should().BeEquivalentTo(directTools.Select(x => x.Name));

		using var revoked = await userA.DeleteAsync($"/api/auth/tokens/{tokenIdA}", ct);
		revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);
		await AssertUnauthorizedAsync(userA, tokenA, ct);
	}

	[Fact]
	public async Task Stdio_DiscoveryAndCalls_PersistAcrossProcessRestartAndIsolateUserFiles()
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(90));
		var ct = timeout.Token;
		var root = Path.Combine(Path.GetTempPath(), "ownplanner-mcp-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			JsonElement task;
			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "user-a"), cancellationToken: ct))
			{
				await AssertDiscoveryAsync(client, ct);
				task = await ExerciseTaskContractAsync(client, ct);
			}

			// Start a new host against the existing SQLite file, including its migration history.
			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "user-a"), cancellationToken: ct))
			{
				var persisted = await CallAsync(client, "taskitem_get", new() { ["id"] = task.GetProperty("id").GetGuid() }, ct);
				JsonElement.DeepEquals(task, persisted).Should().BeTrue();
				var page = await CallAsync(client, "taskitem_list_items", new(), ct);
				page.GetProperty("totalCount").GetInt32().Should().Be(1);
			}

			await using (var client = await McpClient.CreateAsync(CreateStdioTransport(root, "user-b"), cancellationToken: ct))
			{
				var hidden = await CallAsync(client, "taskitem_get", new() { ["id"] = task.GetProperty("id").GetGuid() }, ct);
				hidden.GetProperty("error").GetString().Should().Be("Task not found");
			}
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = application.BaseAddress };

	private HttpClientTransport CreateHttpTransport(string token) => new(new HttpClientTransportOptions
	{
		Endpoint = new Uri(application.BaseAddress, "/mcp"),
		TransportMode = HttpTransportMode.StreamableHttp,
		AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
	});

	private static StdioClientTransport CreateStdioTransport(string root, string user)
	{
		var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
		var assembly = Path.Combine(E2eWebApplicationFactory.FindRepositoryRoot(), "OwnPlanner.Mcp.StdioApp", "bin", configuration, "net10.0", "OwnPlanner.Mcp.StdioApp.dll");
		File.Exists(assembly).Should().BeTrue("the E2E project builds its stdio host dependency");
		return new StdioClientTransport(new StdioClientTransportOptions
		{
			Command = "dotnet",
			Arguments = [assembly, "--user-id", user, "--session-id", Guid.NewGuid().ToString()],
			EnvironmentVariables = new Dictionary<string, string?> { ["MCP_DATA_DIR"] = root, ["MCP_LOG_DIR"] = Path.Combine(root, "logs") },
			ShutdownTimeout = TimeSpan.FromSeconds(10),
		});
	}

	private static async Task<(Guid UserId, string Token, Guid TokenId)> RegisterWithTokenAsync(HttpClient http, CancellationToken ct)
	{
		var suffix = Guid.NewGuid().ToString("N");
		using var response = await http.PostAsJsonAsync("/api/auth/register", new { email = $"mcp-{suffix}@example.test", username = $"mcp-{suffix[..12]}", password = "Test!Password123" }, ct);
		response.EnsureSuccessStatusCode();
		using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
		using var created = await http.PostAsJsonAsync("/api/auth/tokens", new { name = "Transport regression" }, ct);
		created.EnsureSuccessStatusCode();
		using var token = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
		return (user.RootElement.GetProperty("user").GetProperty("id").GetGuid(), token.RootElement.GetProperty("plaintextToken").GetString()!, token.RootElement.GetProperty("token").GetProperty("id").GetGuid());
	}

	private static async Task AssertUnauthorizedAsync(HttpClient http, string? token, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
		{
			Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }),
		};
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Accept.ParseAdd("text/event-stream");
		if (token is not null) request.Headers.Authorization = new("Bearer", token);
		using var response = await http.SendAsync(request, ct);
		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	private static async Task AssertDiscoveryAsync(McpClient client, CancellationToken ct)
	{
		var tools = await client.ListToolsAsync(cancellationToken: ct);
		tools.Select(x => x.Name).Should().Contain(["taskitem_create", "taskitem_get", "taskitem_update", "taskitem_list_items", "goal_create", "noteitem_create"]);
		var schema = tools.Single(x => x.Name == "taskitem_create").JsonSchema;
		schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo(["title", "taskListId"]);
		schema.GetProperty("properties").TryGetProperty("userId", out _).Should().BeFalse();
		tools.Single(x => x.Name == "taskitem_update").JsonSchema.GetProperty("properties").GetProperty("clearDueAt").GetProperty("type").GetString().Should().Be("boolean");
	}

	private static async Task<JsonElement> ExerciseTaskContractAsync(McpClient client, CancellationToken ct)
	{
		const string title = "Transport task — 日本語";
		var created = await CallAsync(client, "taskitem_create", new() { ["title"] = title, ["taskListId"] = WellKnownIds.InboxTaskList, ["dueAt"] = "2026-10-01" }, ct);
		var id = created.GetProperty("id").GetGuid();
		created.GetProperty("title").GetString().Should().Be(title);
		var updated = await CallAsync(client, "taskitem_update", new() { ["id"] = id, ["clearDueAt"] = true }, ct);
		updated.GetProperty("dueAt").ValueKind.Should().Be(JsonValueKind.Null);
		var persisted = await CallAsync(client, "taskitem_get", new() { ["id"] = id }, ct);
		AssertTaskContent(updated, persisted);
		// SQLite round-trips DateTime ticks but does not preserve DateTime.Kind.
		persisted.GetProperty("updatedAt").GetDateTime().Ticks.Should().Be(updated.GetProperty("updatedAt").GetDateTime().Ticks);
		var page = await CallAsync(client, "taskitem_list_items", new() { ["limit"] = 1, ["offset"] = 0 }, ct);
		page.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(["items", "totalCount", "offset", "limit", "hasMore"]);
		page.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(id);
		page.GetProperty("totalCount").GetInt32().Should().Be(1);
		page.GetProperty("hasMore").GetBoolean().Should().BeFalse();
		return persisted;
	}

	private static void AssertTaskContent(JsonElement expected, JsonElement actual)
	{
		// The in-process adapter intentionally omits audit timestamps from model context.
		var expectedContent = JsonNode.Parse(expected.GetRawText())!.AsObject();
		var actualContent = JsonNode.Parse(actual.GetRawText())!.AsObject();
		foreach (var name in new[] { "createdAt", "updatedAt" })
		{
			expectedContent.Remove(name);
			actualContent.Remove(name);
		}
		JsonNode.DeepEquals(expectedContent, actualContent).Should().BeTrue();
	}

	private static async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?> args, CancellationToken ct)
	{
		var result = await client.CallToolAsync(name, args, cancellationToken: ct);
		result.IsError.Should().NotBe(true);
		using var document = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text);
		return document.RootElement.Clone();
	}
}
