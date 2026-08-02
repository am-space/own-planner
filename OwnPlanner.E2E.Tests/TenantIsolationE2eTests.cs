using Microsoft.Playwright;
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
}
