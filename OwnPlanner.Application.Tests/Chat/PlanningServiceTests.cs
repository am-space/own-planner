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
		_chatAdapter.GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("response", 123));
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

	[Fact]
	public void CurrentContextLengthTokens_DelegatesTo_ChatAdapter()
	{
		_chatAdapter.CurrentContextLengthTokens.Returns(456);

		_svc.CurrentContextLengthTokens.Should().Be(456);
	}

	[Fact]
  public void MaxContextLengthTokens_DefaultsTo64K()
	{
	 _svc.MaxContextLengthTokens.Should().Be(64 * 1024);
	}

	// --- SwitchModeAsync ---

	[Fact]
	public async Task SwitchModeAsync_UpdatesCurrentMode()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);

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
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(mode, ct);

		_chatAdapter.Received(1).ResetChatSession(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_CallsEachPreloadTool()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);

		foreach (var tool in ModeConfig.All[PlanningMode.GlobalPlanning].PreloadTools)
			await _mcpAdapter.Received(1).CallToolAsync(tool, Arg.Any<Dictionary<string, object?>?>(), ct);
	}

	[Fact]
	public async Task SwitchModeAsync_InjectsPreloadResultsIntoSystemPrompt()
	{
		var ct = TestContext.Current.CancellationToken;
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct)
			.Returns("goal-data");

		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);

		_chatAdapter.Received(1).ResetChatSession(
			Arg.Is<string?>(p => p != null && p.Contains("goal-data") && p.Contains("## Current context")),
			Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_WithoutMcp_ResetsSessionWithModePromptOnly()
	{
		var ct = TestContext.Current.CancellationToken;
		var svcWithoutMcp = new PlanningService(_chatAdapter, null, _logger);

		await svcWithoutMcp.SwitchModeAsync(PlanningMode.WeekPlanning, ct);

		_chatAdapter.Received(1).ResetChatSession(
			Arg.Is<string?>(p => p != null && p.Contains("Week Planning") && !p.Contains("## Current context")),
			Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task SwitchModeAsync_SystemAnalysis_PassesRestrictedAllowedTools()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.SystemAnalysis, ct);

		var expected = ModeConfig.All[PlanningMode.SystemAnalysis].AllowedTools;
		_chatAdapter.Received(1).ResetChatSession(
			Arg.Any<string?>(),
			Arg.Is<IReadOnlyList<string>?>(t => t != null && t.SequenceEqual(expected)));
	}

	[Fact]
	public async Task SwitchModeAsync_WhenPreloadToolThrows_StillResetsSession()
	{
		var ct = TestContext.Current.CancellationToken;
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct)
			.Throws(new Exception("MCP unavailable"));

		await _svc.SwitchModeAsync(PlanningMode.DayWork, ct);

		_chatAdapter.Received(1).ResetChatSession(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>());
	}

	// --- GetResponseAsync ---

	[Fact]
	public async Task GetResponseAsync_BeforeModeSwitch_ActivatesDefaultModeAndForwardsMessageDirectly()
	{
		var ct = TestContext.Current.CancellationToken;

		string? captured = null;
	  _chatAdapter.GetResponse(Arg.Do<string>(m => captured = m), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("ok", 111));

		await _svc.GetResponseAsync("hello", ct);

		_chatAdapter.Received(1).ResetChatSession(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>());
		captured.Should().Be("hello");
	}

	[Fact]
	public async Task GetResponseAsync_AfterNonRefreshMode_ForwardsMessageDirectly()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);
		_mcpAdapter.ClearReceivedCalls();

		await _svc.GetResponseAsync("hello", ct);

		await _chatAdapter.Received(1).GetResponse("hello", Arg.Any<CancellationToken>());
		await _mcpAdapter.DidNotReceive().CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct);
	}

	[Fact]
	public async Task GetResponseAsync_AfterDayWork_ForwardsMessageDirectlyWithoutPerTurnPreload()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.DayWork, ct);
		_mcpAdapter.ClearReceivedCalls();

		string? captured = null;
	  _chatAdapter.GetResponse(Arg.Do<string>(m => captured = m), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("ok", 111));

		await _svc.GetResponseAsync("what should I do?", ct);

		captured.Should().Be("what should I do?");
		await _mcpAdapter.DidNotReceive().CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct);
	}

	[Fact]
	public async Task GetResponseAsync_DayWork_WithoutMcp_ForwardsMessageDirectly()
	{
		var ct = TestContext.Current.CancellationToken;
		var svcWithoutMcp = new PlanningService(_chatAdapter, null, _logger);
		await svcWithoutMcp.SwitchModeAsync(PlanningMode.DayWork, ct);

		await svcWithoutMcp.GetResponseAsync("hello", ct);

		await _chatAdapter.Received(1).GetResponse("hello", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_DayWork_WhenAllToolsThrow_ForwardsOriginalMessage()
	{
		var ct = TestContext.Current.CancellationToken;
		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct)
			.Throws(new Exception("timeout"));
		await _svc.SwitchModeAsync(PlanningMode.DayWork, ct);

		_mcpAdapter.CallToolAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>?>(), ct)
			.Throws(new Exception("timeout"));

		await _svc.GetResponseAsync("hello", ct);

		await _chatAdapter.Received(1).GetResponse("hello", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_ReturnsResponseFromChatAdapter()
	{
		var ct = TestContext.Current.CancellationToken;
		_chatAdapter.GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("the answer", 789));

		var result = await _svc.GetResponseAsync("question", ct);

		result.Message.Should().Be("the answer");
		result.ContextLengthTokens.Should().Be(789);
	}

	[Fact]
	public async Task GetResponseAsync_WhenCurrentContextIsAtLimit_Throws()
	{
		var ct = TestContext.Current.CancellationToken;
		_chatAdapter.CurrentContextLengthTokens.Returns(64 * 1024);

		var action = () => _svc.GetResponseAsync("hello", ct);

		await action.Should().ThrowAsync<ChatContextLimitExceededException>();
		await _chatAdapter.DidNotReceive().GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_WhenProjectedContextExceedsLimit_Throws()
	{
		var ct = TestContext.Current.CancellationToken;
		var svcWithSmallLimit = new PlanningService(_chatAdapter, null, _logger, maxContextLengthTokens: 10);
		_chatAdapter.CurrentContextLengthTokens.Returns(9);

		await svcWithSmallLimit.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);

		var action = () => svcWithSmallLimit.GetResponseAsync("12345678", ct);

		await action.Should().ThrowAsync<ChatContextLimitExceededException>();
		await _chatAdapter.DidNotReceive().GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_UsesProjectedAssistantTokensForNextTurnLimitCheck()
	{
		var ct = TestContext.Current.CancellationToken;
		var svcWithSmallLimit = new PlanningService(_chatAdapter, null, _logger, maxContextLengthTokens: 10);
		_chatAdapter.CurrentContextLengthTokens.Returns(8);
		_chatAdapter.GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("12345678", 8));

		await svcWithSmallLimit.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);
		await svcWithSmallLimit.GetResponseAsync("hi", ct);

		var action = () => svcWithSmallLimit.GetResponseAsync("ok", ct);

		await action.Should().ThrowAsync<ChatContextLimitExceededException>();
		await _chatAdapter.Received(1).GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	// --- history compaction ---

	private PlanningService CreateCompactingService(HistoryCompactionStrategy strategy = HistoryCompactionStrategy.Summarize) =>
		new(_chatAdapter, _mcpAdapter, _logger,
			maxContextLengthTokens: 100, compactionThresholdRatio: 0.7, recentTurnsToKeep: 1, compactionStrategy: strategy);

	private async Task BuildTranscriptAsync(PlanningService svc, CancellationToken ct, params string[] messages)
	{
		_chatAdapter.GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChatTurnResult("resp", 10));
		_chatAdapter.CurrentContextLengthTokens.Returns(10); // comfortably below the soft threshold
		await svc.SwitchModeAsync(PlanningMode.DayWork, ct);
		foreach (var message in messages)
			await svc.GetResponseAsync(message, ct);
	}

	[Fact]
	public async Task GetResponseAsync_BelowSoftThreshold_DoesNotCompact()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService();

		await BuildTranscriptAsync(svc, ct, "m1", "m2", "m3");

		await _chatAdapter.DidNotReceive().SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		_chatAdapter.DidNotReceive().RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>());
	}

	[Fact]
	public async Task GetResponseAsync_OverSoftThreshold_SummarizesOlderTurnsAndRebuilds()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService();
		_chatAdapter.SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("SUMMARY");

		await BuildTranscriptAsync(svc, ct, "m1", "m2"); // transcript: U:m1, M:resp, U:m2, M:resp

		IReadOnlyList<ChatMessage>? captured = null;
		_chatAdapter
			.When(a => a.RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>()))
			.Do(c => captured = c.Arg<IReadOnlyList<ChatMessage>>());
		_chatAdapter.CurrentContextLengthTokens.Returns(80); // > soft threshold (70)

		await svc.GetResponseAsync("m3", ct);

		await _chatAdapter.Received(1).SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		captured.Should().NotBeNull();
		captured!.Should().HaveCount(4); // summary pair + last 1 turn (2 messages)
		captured[0].Role.Should().Be(ChatRole.User);
		captured[0].Text.Should().Contain("SUMMARY");
		captured.Should().Contain(m => m.Text == "m2");      // recent turn retained
		captured.Should().NotContain(m => m.Text == "m1");   // older turn summarized away
	}

	[Fact]
	public async Task GetResponseAsync_WhenSummarizeFails_FallsBackToTrim()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService();
		_chatAdapter.SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<string>(_ => throw new Exception("summarizer down"));

		await BuildTranscriptAsync(svc, ct, "m1", "m2");

		IReadOnlyList<ChatMessage>? captured = null;
		_chatAdapter
			.When(a => a.RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>()))
			.Do(c => captured = c.Arg<IReadOnlyList<ChatMessage>>());
		_chatAdapter.CurrentContextLengthTokens.Returns(80);

		await svc.GetResponseAsync("m3", ct);

		captured.Should().NotBeNull();
		captured!.Should().HaveCount(2); // recent turn only, no summary entry
		captured.Should().NotContain(m => m.Text.Contains("summary"));
		captured.Should().Contain(m => m.Text == "m2");
	}

	[Fact]
	public async Task GetResponseAsync_WithTrimStrategy_DropsOlderTurnsWithoutSummarizing()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService(HistoryCompactionStrategy.Trim);

		await BuildTranscriptAsync(svc, ct, "m1", "m2");

		IReadOnlyList<ChatMessage>? captured = null;
		_chatAdapter
			.When(a => a.RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>()))
			.Do(c => captured = c.Arg<IReadOnlyList<ChatMessage>>());
		_chatAdapter.CurrentContextLengthTokens.Returns(80);

		await svc.GetResponseAsync("m3", ct);

		await _chatAdapter.DidNotReceive().SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		captured.Should().NotBeNull();
		captured!.Should().HaveCount(2); // recent turn only
		captured.Should().Contain(m => m.Text == "m2");
	}

	[Fact]
	public async Task GetResponseAsync_WhenMessageAloneExceedsBudget_ThrowsWithoutCompacting()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService(); // maxContextLengthTokens: 100
		_chatAdapter.SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("SUMMARY");

		// Build a transcript so there *is* older history that could be compacted.
		await BuildTranscriptAsync(svc, ct, "m1", "m2");

		// A single message larger than the whole budget (>100 tokens ≈ >400 chars). Compaction can't help.
		var hugeMessage = new string('x', 1000);
		var action = () => svc.GetResponseAsync(hugeMessage, ct);

		await action.Should().ThrowAsync<ChatContextLimitExceededException>();
		await _chatAdapter.DidNotReceive().SummarizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
		_chatAdapter.DidNotReceive().RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>());
		await _chatAdapter.DidNotReceive().GetResponse(hugeMessage, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetResponseAsync_OverHardLimit_WithNothingToCompact_Throws()
	{
		var ct = TestContext.Current.CancellationToken;
		var svc = CreateCompactingService();
		_chatAdapter.CurrentContextLengthTokens.Returns(100); // at hard limit, transcript empty

		await svc.SwitchModeAsync(PlanningMode.DayWork, ct);

		var action = () => svc.GetResponseAsync("hello", ct);

		await action.Should().ThrowAsync<ChatContextLimitExceededException>();
		_chatAdapter.DidNotReceive().RebuildSession(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<IReadOnlyList<ChatMessage>>());
		await _chatAdapter.DidNotReceive().GetResponse(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	// --- DisposeAsync ---

	[Fact]
	public async Task DisposeAsync_DisposesUnderlyingChatAdapter()
	{
		var ct = TestContext.Current.CancellationToken;
		ct.ThrowIfCancellationRequested();
		await _svc.DisposeAsync();

		await _chatAdapter.Received(1).DisposeAsync();
	}
}
