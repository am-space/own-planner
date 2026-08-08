using FluentAssertions;
using Microsoft.Playwright;
using System.Text.Json;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class TenantIsolationE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Fact]
	public async Task TwoAuthenticatedBrowserContexts_CannotSeeEachOthersTasks()
	{
		await RegisterAsync(Page, CreateUser());
		var taskTitle = $"Private E2E task {Guid.NewGuid():N}";
		var createPrompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter ?? throw new InvalidOperationException("MCP adapter is required.");
			await mcp.CallToolAsync(
				"taskitem_create",
				new Dictionary<string, object?>
				{
					["title"] = taskTitle,
					["taskListId"] = WellKnownIds.InboxTaskList,
				});
			return new ChatTurnResult($"Created {taskTitle}", 100);
		});
		await SendPromptAsync(Page, createPrompt);
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		await using var secondContext = await Browser.NewContextAsync(CreateContextOptions());
		var secondUserPage = await secondContext.NewPageAsync();
		await RegisterAsync(secondUserPage, CreateUser());
		var listPrompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter ?? throw new InvalidOperationException("MCP adapter is required.");
			var result = await mcp.CallToolAsync("taskitem_list_items");
			if (result.Contains(taskTitle, StringComparison.Ordinal))
			{
				throw new InvalidOperationException($"User B received user A's task '{taskTitle}'.");
			}

			return new ChatTurnResult("User B task list is isolated.", 100);
		});

		await SendPromptAsync(secondUserPage, listPrompt);

		await Expect(secondUserPage.GetByText("User B task list is isolated.", new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task PlannerApi_IsolatesEveryReadSurface_AndRejectsUnauthenticatedRequests()
	{
		await RegisterAsync(Page, CreateUser());
		var suffix = Guid.NewGuid().ToString("N");
		var taskTitle = $"Private planner task {suffix}";
		var goalTitle = $"Private planner goal {suffix}";
		var noteTitle = $"Private planner note {suffix}";
		var createPrompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var mcp = mcpAdapter ?? throw new InvalidOperationException("MCP adapter is required.");
			await mcp.CallToolAsync("taskitem_create", new Dictionary<string, object?>
			{
				["title"] = taskTitle,
				["taskListId"] = WellKnownIds.InboxTaskList,
			});
			await mcp.CallToolAsync("goal_create", new Dictionary<string, object?>
			{
				["title"] = goalTitle,
				["horizon"] = "Yearly",
				["targetPeriod"] = "2026",
			});
			await mcp.CallToolAsync("noteitem_create", new Dictionary<string, object?>
			{
				["title"] = noteTitle,
				["noteListId"] = WellKnownIds.InboxNoteList,
			});
			return new ChatTurnResult("Created private planner data", 100);
		});
		await SendPromptAsync(Page, createPrompt);
		await Expect(Page.GetByText("Created private planner data", new() { Exact = true })).ToBeVisibleAsync();

		var tasks = await FetchPlannerAsync(Page, "/api/planner/tasks?status=all");
		var goals = await FetchPlannerAsync(Page, "/api/planner/goals?status=all");
		var notes = await FetchPlannerAsync(Page, "/api/planner/notes");
		var options = await FetchPlannerAsync(Page, "/api/planner/filter-options");
		tasks.Status.Should().Be(200);
		goals.Status.Should().Be(200);
		notes.Status.Should().Be(200);
		options.Status.Should().Be(200);
		var taskId = FindItemId(tasks.Body, taskTitle);
		var goalId = FindItemId(goals.Body, goalTitle);
		var noteId = FindItemId(notes.Body, noteTitle);
		options.Body.Should().Contain(goalTitle);

		await using var secondContext = await Browser.NewContextAsync(CreateContextOptions());
		var secondPage = await secondContext.NewPageAsync();
		await RegisterAsync(secondPage, CreateUser());
		foreach (var path in new[]
		{
			"/api/planner/tasks?status=all",
			"/api/planner/goals?status=all",
			"/api/planner/notes",
			"/api/planner/filter-options",
		})
		{
			var response = await FetchPlannerAsync(secondPage, path);
			response.Status.Should().Be(200);
			response.Body.Should().NotContain(taskTitle).And.NotContain(goalTitle).And.NotContain(noteTitle);
		}

		(await FetchPlannerAsync(secondPage, $"/api/planner/tasks/{taskId}")).Status.Should().Be(404);
		(await FetchPlannerAsync(secondPage, $"/api/planner/goals/{goalId}")).Status.Should().Be(404);
		(await FetchPlannerAsync(secondPage, $"/api/planner/notes/{noteId}")).Status.Should().Be(404);
		(await FetchPlannerAsync(secondPage, "/api/planner/tasks?offset=-1")).Status.Should().Be(400);
		(await FetchPlannerAsync(secondPage, "/api/planner/tasks/not-a-guid")).Status.Should().Be(400);

		await using var anonymousContext = await Browser.NewContextAsync(CreateContextOptions());
		var anonymousPage = await anonymousContext.NewPageAsync();
		await anonymousPage.GotoAsync("/login");
		(await FetchPlannerAsync(anonymousPage, "/api/planner/tasks")).Status.Should().Be(401);
	}

	private static async Task<BrowserApiResponse> FetchPlannerAsync(IPage page, string path)
	{
		var serialized = await page.EvaluateAsync<string>(
			"""async (url) => { const response = await fetch(url, { credentials: 'include' }); return JSON.stringify({ status: response.status, body: await response.text() }); }""",
			path);
		return JsonSerializer.Deserialize<BrowserApiResponse>(serialized, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
		}) ?? throw new InvalidOperationException("Browser API response could not be parsed.");
	}

	private static Guid FindItemId(string responseBody, string title)
	{
		using var document = JsonDocument.Parse(responseBody);
		foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
		{
			if (item.GetProperty("title").GetString() == title)
			{
				return item.GetProperty("id").GetGuid();
			}
		}

		throw new InvalidOperationException($"Planner response did not contain '{title}'.");
	}

	private sealed record BrowserApiResponse(int Status, string Body);
}
