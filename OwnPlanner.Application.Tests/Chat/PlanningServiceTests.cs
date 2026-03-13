using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public class PlanningServiceTests
{
	private readonly IChatAdapter _chatAdapter = Substitute.For<IChatAdapter>();
	private readonly IMcpAdapter _mcpAdapter = Substitute.For<IMcpAdapter>();
	private readonly ILogger<PlanningService> _logger = Substitute.For<ILogger<PlanningService>>();
	private readonly PlanningService _svc;

	public PlanningServiceTests()
	{
		_chatAdapter.GetResponse(Arg.Any<string>()).Returns("response");
		_chatAdapter.DisposeAsync().Returns(ValueTask.CompletedTask);
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Returns("tool-result");
		_mcpAdapter.DisposeAsync().Returns(ValueTask.CompletedTask);
		_svc = new PlanningService(_chatAdapter, _mcpAdapter, _logger);
	}

	// --- default state ---

	[Fact]
	public void CurrentMode_BeforeSwitch_IsDayWork()
	{
		_svc.CurrentMode.Should().Be(PlanningMode.DayWork);
	}

	[Fact]
	public void CreatedTime_DelegatesTo_ChatAdapter()
	{
		var expected = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		_chatAdapter.CreatedTime.Returns(expected);

		_svc.CreatedTime.Should().Be(expected);
	}

	[Fact]
	public void LastAccessTime_DelegatesTo_ChatAdapter()
	{
		var expected = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
		_chatAdapter.LastAccessTime.Returns(expected);

		_svc.LastAccessTime.Should().Be(expected);
	}

	// --- SwitchModeAsync ---

	[Fact]
	public async Task SwitchModeAsync_UpdatesCurrentMode()
	{
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning);

		_svc.CurrentMode.Should().Be(PlanningMode.GlobalPlanning);
	}

	[Theory]
	[InlineData(PlanningMode.GlobalPlanning)]
	[InlineData(PlanningMode.WeekPlanning)]
	[InlineData(PlanningMode.DayWork)]
	[InlineData(PlanningMode.Reflection)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task SwitchModeAsync_CallsResetChatSession_ForEveryMode(PlanningMode mode)
	{
		await _svc.SwitchModeAsync(mode);

		_chatAdapter.Received(1).ResetChatSession(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_CallsEachPreloadTool()
	{
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning);

		foreach (var tool in ModeConfig.All[PlanningMode.GlobalPlanning].PreloadTools)
			await _mcpAdapter.Received(1).CallToolAsync(tool, Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SwitchModeAsync_InjectsPreloadResultsIntoSystemPrompt()
	{
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Returns("goal-data");

		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning);

		_chatAdapter.Received(1).ResetChatSession(
			Arg.Is<string?>(p => p != null && p.Contains("goal-data") && p.Contains("## Current context")),
			Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_WithoutMcp_ResetsSessionWithModePromptOnly()
	{
		var svcWithoutMcp = new PlanningService(_chatAdapter, null, _logger);

		await svcWithoutMcp.SwitchModeAsync(PlanningMode.WeekPlanning);

		_chatAdapter.Received(1).ResetChatSession(
			Arg.Is<string?>(p => p != null && p.Contains("Week Planning") && !p.Contains("## Current context")),
			Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_SystemAnalysis_PassesRestrictedAllowedTools()
	{
		await _svc.SwitchModeAsync(PlanningMode.SystemAnalysis);

		var expected = ModeConfig.All[PlanningMode.SystemAnalysis].AllowedTools;
		_chatAdapter.Received(1).ResetChatSession(
			Arg.Any<string?>(),
			Arg.Is<IReadOnlyList<string>?>(t => t != null && t.SequenceEqual(expected)));
	}

	[Fact]
	public async Task SwitchModeAsync_WhenPreloadToolThrows_StillResetsSession()
	{
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Throws(new Exception("MCP unavailable"));

		await _svc.SwitchModeAsync(PlanningMode.DayWork);

		_chatAdapter.Received(1).ResetChatSession(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>());
	}

	// --- GetResponseAsync ---

	[Fact]
	public async Task GetResponseAsync_BeforeModeSwitch_ForwardsMessageDirectly()
	{
		await _svc.GetResponseAsync("hello");

		await _chatAdapter.Received(1).GetResponse("hello");
		await _mcpAdapter.DidNotReceive().CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_AfterNonRefreshMode_ForwardsMessageDirectly()
	{
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning);
		_mcpAdapter.ClearReceivedCalls();

		await _svc.GetResponseAsync("hello");

		await _chatAdapter.Received(1).GetResponse("hello");
		await _mcpAdapter.DidNotReceive().CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_AfterDayWork_PrependsRefreshedContext()
	{
		await _svc.SwitchModeAsync(PlanningMode.DayWork);
		_mcpAdapter.ClearReceivedCalls();
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Returns("today-tasks");

		string? captured = null;
		_chatAdapter.GetResponse(Arg.Do<string>(m => captured = m)).Returns("ok");

		await _svc.GetResponseAsync("what should I do?");

		captured.Should().Contain("[Refreshed context]");
		captured.Should().Contain("today-tasks");
		captured.Should().Contain("[User message]");
		captured.Should().Contain("what should I do?");
	}

	[Fact]
	public async Task GetResponseAsync_DayWork_WithoutMcp_ForwardsMessageDirectly()
	{
		var svcWithoutMcp = new PlanningService(_chatAdapter, null, _logger);
		await svcWithoutMcp.SwitchModeAsync(PlanningMode.DayWork);

		await svcWithoutMcp.GetResponseAsync("hello");

		await _chatAdapter.Received(1).GetResponse("hello");
	}

	[Fact]
	public async Task GetResponseAsync_DayWork_WhenAllToolsThrow_ForwardsOriginalMessage()
	{
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Throws(new Exception("timeout"));
		await _svc.SwitchModeAsync(PlanningMode.DayWork);

		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), Arg.Any<CancellationToken>())
			.Throws(new Exception("timeout"));

		await _svc.GetResponseAsync("hello");

		await _chatAdapter.Received(1).GetResponse("hello");
	}

	[Fact]
	public async Task GetResponseAsync_ReturnsResponseFromChatAdapter()
	{
		_chatAdapter.GetResponse(Arg.Any<string>()).Returns("the answer");

		var result = await _svc.GetResponseAsync("question");

		result.Should().Be("the answer");
	}

	// --- DisposeAsync ---

	[Fact]
	public async Task DisposeAsync_DisposesUnderlyingChatAdapter()
	{
		await _svc.DisposeAsync();

		await _chatAdapter.Received(1).DisposeAsync();
	}
}
