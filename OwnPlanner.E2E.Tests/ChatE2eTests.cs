using System.Text.Json;
using Microsoft.Playwright;
using OwnPlanner.Application.Chat;
using OwnPlanner.Domain;
using OwnPlanner.E2E.Tests.Infrastructure;

namespace OwnPlanner.E2E.Tests;

[Collection(E2eCollection.Name)]
[Trait("Category", "E2E")]
public sealed class ChatE2eTests(E2eWebApplicationFactory application) : E2ePageTest(application)
{
	[Fact]
	public async Task ScriptedResponse_RendersThroughRealChatApi()
	{
		await RegisterAsync(Page, CreateUser());
		const string assistantResponse = "Deterministic E2E response.";
		var prompt = Application.Scenarios.RegisterResponse(assistantResponse);

		await SendPromptAsync(Page, prompt);

		await Expect(Page.GetByText(assistantResponse, new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task TaskCreatedThroughMcp_PersistsAfterChatSessionIsCleared()
	{
		await RegisterAsync(Page, CreateUser());
		var taskTitle = $"E2E task {Guid.NewGuid():N}";
		var createPrompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var result = await RequireMcp(mcpAdapter).CallToolAsync(
				"taskitem_create",
				new Dictionary<string, object?>
				{
					["title"] = taskTitle,
					["taskListId"] = WellKnownIds.InboxTaskList,
				});
			EnsureTaskPresence(result, taskTitle, expected: true);
			return new ChatTurnResult($"Created {taskTitle}", 120);
		});

		await SendPromptAsync(Page, createPrompt);
		await Expect(Page.GetByText($"Created {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();

		await Page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!" })).ToBeVisibleAsync();

		var listPrompt = Application.Scenarios.Register(async mcpAdapter =>
		{
			var result = await RequireMcp(mcpAdapter).CallToolAsync("taskitem_list_items");
			EnsureTaskPresence(result, taskTitle, expected: true);
			return new ChatTurnResult($"Found {taskTitle}", 140);
		});
		await SendPromptAsync(Page, listPrompt);

		await Expect(Page.GetByText($"Found {taskTitle}", new() { Exact = true })).ToBeVisibleAsync();
	}

	[Fact]
	public async Task ScriptedProviderFailure_ShowsExistingUserFacingErrorAndRestoresInput()
	{
		await RegisterAsync(Page, CreateUser());
		var prompt = Application.Scenarios.Register(_ => throw new InvalidOperationException("Scripted provider failure"));

		await SendPromptAsync(Page, prompt);

		await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("An error occurred while processing your message");
		await Expect(Page.GetByPlaceholder("Type your message... (Enter to send, Shift+Enter for new line)")).ToHaveValueAsync(prompt);
	}

	private static IMcpAdapter RequireMcp(IMcpAdapter? mcpAdapter) =>
		mcpAdapter ?? throw new InvalidOperationException("The scripted E2E adapter did not receive the real MCP adapter.");

	private static void EnsureTaskPresence(string json, string taskTitle, bool expected)
	{
		using var document = JsonDocument.Parse(json);
		var containsTask = document.RootElement.ValueKind == JsonValueKind.Object &&
			(document.RootElement.TryGetProperty("title", out var createdTitle) && createdTitle.GetString() == taskTitle ||
			 document.RootElement.TryGetProperty("items", out var items) && items.EnumerateArray().Any(item => item.GetProperty("title").GetString() == taskTitle));
		if (containsTask != expected)
		{
			throw new InvalidOperationException(
				expected ? $"Expected tool result to contain task '{taskTitle}'." : $"Tool result exposed task '{taskTitle}' to another user.");
		}
	}
}
