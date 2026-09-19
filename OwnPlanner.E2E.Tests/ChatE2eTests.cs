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

	[Theory]
	[InlineData("light", 1280)]
	[InlineData("dark", 1280)]
	[InlineData("light", 320)]
	[InlineData("dark", 320)]
	public async Task MarkdownReport_ContainsOverflowAndPreservesChatInteractions(string theme, int width)
	{
		await Page.AddInitScriptAsync($"localStorage.setItem('ownplanner-color-mode', '{theme}')");
		await Page.SetViewportSizeAsync(width, 900);
		await RegisterAsync(Page, CreateUser());
		var report = """
			# System Diagnostic Report

			**As of:** September 19, 2026\
			**Mode:** System Analysis

			## Executive summary

			The system tracks **9 contexts**, **13 active goals**, and **106 incomplete tasks**.

			A separate paragraph gives the report room to breathe, with *emphasis* and ~~superseded findings~~.

			1. **Goal connectivity:** Review tasks without an active goal. This longer finding wraps naturally without losing the alignment of the numbered list.
			   - Check nested items and their relationship to the parent.
			     1. Keep the hierarchy visible.
			2. **Context infrastructure:** Review empty lists.

			> A diagnostic observation with a separate paragraph.
			>
			> Keep supporting context readable.

			### Next actions
			- [x] Review current goals
			- [ ] Connect remaining tasks

			#### Supporting details
			Use `taskitem_list_items` to inspect tasks.

			##### Source
			[Planning reference](https://example.test/planning)

			###### Notes
			Full reports retain every section.

			---

			| Context | Active goals | Open tasks | Next action | Owner | Status |
			| --- | ---: | ---: | --- | --- | --- |
			| Personal | 4 | 29 | Review orphan tasks | You | In progress |
			| Work | 9 | 77 | Review links | You | Ready |

			```json
			{ "description": "A deliberately long code line preserves whitespace and scrolls inside its container rather than widening the conversation", "tasks": 106 }
			```

			https://example.test/abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz

			<script>window.untrustedMarkdown = true</script>

			Report complete.
			""";
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var prompt = Application.Scenarios.Register(async _ =>
		{
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
			return new ChatTurnResult(report, 180);
		});
		await SendPromptAsync(Page, prompt);
		try
		{
			await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Thinking...");
			await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send message" })).ToBeDisabledAsync();
		}
		finally { ready.TrySetResult(); }
		var message = Page.GetByTestId("assistant-message");
		await Expect(message.GetByRole(AriaRole.Heading, new() { Name = "System Diagnostic Report" })).ToBeVisibleAsync();
		Assert.Equal(6, await message.Locator("h1,h2,h3,h4,h5,h6").CountAsync());
		Assert.Equal(2, await message.GetByRole(AriaRole.Checkbox).CountAsync());
		Assert.False(await Page.EvaluateAsync<bool>("() => Boolean(window.untrustedMarkdown)"));
		Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
		Assert.True(await Page.GetByTestId("chat-messages").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth"));
		var code = message.GetByLabel("Code block");
		Assert.True(await code.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth"));
		await code.FocusAsync();
		await Expect(code).ToBeFocusedAsync();
		if (width == 320)
			Assert.True(await message.GetByRole(AriaRole.Region).EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth"));
		await message.GetByRole(AriaRole.Link, new() { Name = "Planning reference" }).FocusAsync();
		await Expect(message.GetByRole(AriaRole.Link, new() { Name = "Planning reference" })).ToBeFocusedAsync();
		await Page.GetByTestId("chat-messages").EvaluateAsync("el => { el.style.scrollBehavior = 'auto'; el.scrollTop = 0; }");
		Directory.CreateDirectory(Application.ArtifactDirectory);
		await Page.ScreenshotAsync(new() { Path = Path.Combine(Application.ArtifactDirectory, $"chat-report-{theme}-{width}.png") });
		await message.GetByText("Report complete.", new() { Exact = true }).ScrollIntoViewIfNeededAsync();
		var last = await message.GetByText("Report complete.", new() { Exact = true }).BoundingBoxAsync();
		var composer = await Page.GetByTestId("chat-composer").BoundingBoxAsync();
		Assert.True(last!.Y + last.Height <= composer!.Y);

		var input = Page.GetByRole(AriaRole.Textbox, new() { Name = "Message", Exact = true });
		await input.FillAsync("First line");
		await input.PressAsync("Shift+Enter");
		await input.PressSequentiallyAsync("Second line");
		await Expect(input).ToHaveValueAsync("First line\nSecond line");
		await Page.ScreenshotAsync(new() { Path = Path.Combine(Application.ArtifactDirectory, $"chat-details-{theme}-{width}.png") });
		await input.FillAsync(Application.Scenarios.RegisterResponse("Short reply."));
		await input.PressAsync("Enter");
		await Expect(Page.GetByText("Short reply.", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByTestId("chat-messages").EvaluateAsync("el => el.scrollTo({ top: el.scrollHeight, behavior: 'instant' })");
		await Page.ScreenshotAsync(new() { Path = Path.Combine(Application.ArtifactDirectory, $"chat-short-{theme}-{width}.png") });
		await SendPromptAsync(Page, Application.Scenarios.RegisterResponse(string.Empty));
		await Expect(Page.GetByTestId("assistant-message")).ToHaveCountAsync(3);
		await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send message" })).ToBeDisabledAsync();
		if (width == 1280)
		{
			await Page.GetByRole(AriaRole.Button, new() { Name = "Tasks", Exact = true }).ClickAsync();
			await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Assistant", Exact = true })).ToBeVisibleAsync();
			await Page.GetByRole(AriaRole.Button, new() { Name = "Clear chat session", Exact = true }).ClickAsync();
			await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!" })).ToBeVisibleAsync();
			Assert.True((await Page.GetByTestId("chat-messages").BoundingBoxAsync())!.Height > 150);
			await Page.GetByTestId("chat-messages").EvaluateAsync("el => el.scrollTo({ top: 0, behavior: 'instant' })");
			await Page.ScreenshotAsync(new() { Path = Path.Combine(Application.ArtifactDirectory, $"chat-compact-{theme}.png") });
			await Page.GetByRole(AriaRole.Button, new() { Name = "Chat", Exact = true }).ClickAsync();
			await Expect(Page).ToHaveURLAsync(new Regex("/chat$"));
		}
		await Page.GetByRole(AriaRole.Combobox, new() { Name = "Planning mode" }).ClickAsync();
		await Page.GetByRole(AriaRole.Option, new() { Name = "Reflection", Exact = true }).ClickAsync();
		await Expect(Page.GetByText("Switched to Reflection", new() { Exact = true })).ToBeVisibleAsync();
		await Page.GetByRole(AriaRole.Button, new() { Name = width == 320 ? "Clear chat session" : "Clear", Exact = true }).ClickAsync();
		await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to OwnPlanner Chat!" })).ToBeVisibleAsync();
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
