using System.Text;
using Microsoft.Extensions.Logging;

namespace OwnPlanner.Application.Chat;

public sealed class PlanningService : IPlanningService
{
	private readonly IChatAdapter _chatAdapter;
	private readonly IMcpAdapter? _mcpAdapter;
	private readonly ILogger<PlanningService> _logger;
	private readonly int _maxContextLengthTokens;
	private readonly int _softThresholdTokens;
	private readonly int _recentMessagesToKeep;
	private readonly HistoryCompactionStrategy _compactionStrategy;

	private PlanningMode _currentMode = PlanningMode.DayWork;
	private ModeConfig _currentConfig = ModeConfig.All[PlanningMode.DayWork];
	private bool _modeActivated;
	private string _currentSystemPrompt = string.Empty;
	// Plain-text transcript of completed turns, used to summarize/trim and rebuild the session on compaction.
	private readonly List<ChatMessage> _transcript = [];
	// PromptTokenCount reflects prompt-side usage only; keep a local next-turn projection that also includes assistant output.
	private int? _projectedContextLengthTokens;

	public PlanningService(
		IChatAdapter chatAdapter,
		IMcpAdapter? mcpAdapter,
		ILogger<PlanningService> logger,
		int maxContextLengthTokens = 64 * 1024,
		double compactionThresholdRatio = 0.7,
		int recentTurnsToKeep = 3,
		HistoryCompactionStrategy compactionStrategy = HistoryCompactionStrategy.Summarize)
	{
		if (maxContextLengthTokens <= 0)
			throw new ArgumentOutOfRangeException(nameof(maxContextLengthTokens), "maxContextLengthTokens must be greater than 0.");
		if (compactionThresholdRatio is <= 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(compactionThresholdRatio), "compactionThresholdRatio must be in the range (0, 1].");
		if (recentTurnsToKeep <= 0)
			throw new ArgumentOutOfRangeException(nameof(recentTurnsToKeep), "recentTurnsToKeep must be greater than 0.");

		_chatAdapter = chatAdapter;
		_mcpAdapter = mcpAdapter;
		_logger = logger;
		_maxContextLengthTokens = maxContextLengthTokens;
		_softThresholdTokens = Math.Max(1, (int)(maxContextLengthTokens * compactionThresholdRatio));
		_recentMessagesToKeep = recentTurnsToKeep * 2;
		_compactionStrategy = compactionStrategy;
	}

	public DateTime CreatedTime => _chatAdapter.CreatedTime;
	public DateTime LastAccessTime => _chatAdapter.LastAccessTime;
	public int? CurrentContextLengthTokens => _chatAdapter.CurrentContextLengthTokens;
	public int MaxContextLengthTokens => _maxContextLengthTokens;
	public PlanningMode CurrentMode => _currentMode;

	public async Task SwitchModeAsync(PlanningMode mode, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Switching planning mode from {OldMode} to {NewMode}", _currentMode, mode);

		var config = ModeConfig.All[mode];
		var context = await LoadContextAsync(config, cancellationToken);
		var systemPrompt = BuildSystemPrompt(config, context);

		_chatAdapter.ResetChatSession(systemPrompt, config.AllowedTools);

		_currentMode = mode;
		_currentConfig = config;
		_currentSystemPrompt = systemPrompt;
		_modeActivated = true;
		_projectedContextLengthTokens = null;
		_transcript.Clear();

		_logger.LogInformation("Planning mode switched to {Mode}", mode);
	}

	public async Task<ChatTurnResult> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
	{
		if (!_modeActivated)
		{
			await SwitchModeAsync(_currentMode, cancellationToken).ConfigureAwait(false);
		}

		await EnsureContextWithinLimitAsync(userMessage, cancellationToken).ConfigureAwait(false);

		var result = await _chatAdapter.GetResponse(userMessage, cancellationToken);

		_transcript.Add(new ChatMessage(ChatRole.User, userMessage));
		_transcript.Add(new ChatMessage(ChatRole.Model, result.Message));

		var assistantResponseTokens = EstimateTokenCount(result.Message);
		_projectedContextLengthTokens = result.ContextLengthTokens is int promptTokens
			? promptTokens + assistantResponseTokens
			: null;

		return result;
	}

	/// <summary>
	/// Compacts older history when the next turn would approach the context limit. Throws only when the
	/// limit would be exceeded and there is nothing left to compact (e.g. a single turn larger than the budget).
	/// </summary>
	private async Task EnsureContextWithinLimitAsync(string message, CancellationToken cancellationToken)
	{
		var currentContextLengthTokens = _chatAdapter.CurrentContextLengthTokens;
		var effectiveContextLengthTokens = Math.Max(currentContextLengthTokens ?? 0, _projectedContextLengthTokens ?? 0);
		var estimatedMessageTokens = EstimateTokenCount(message);
		var projectedContextLengthTokens = effectiveContextLengthTokens + estimatedMessageTokens;

		if (projectedContextLengthTokens < _softThresholdTokens)
		{
			return;
		}

		// The message alone exceeds the budget — compaction frees history, not the incoming turn, so it
		// can never make room. Fail fast rather than letting the chat SDK reject the oversized request.
		if (estimatedMessageTokens > _maxContextLengthTokens)
		{
			_logger.LogWarning(
				"Chat context limit exceeded by the message itself. EstimatedMessageTokens={MessageTokens}, Max={Max}",
				estimatedMessageTokens,
				_maxContextLengthTokens);

			throw new ChatContextLimitExceededException(estimatedMessageTokens, _maxContextLengthTokens);
		}

		_logger.LogInformation(
			"Chat context approaching limit (projected {Projected} >= soft threshold {Soft} of max {Max}); attempting compaction.",
			projectedContextLengthTokens,
			_softThresholdTokens,
			_maxContextLengthTokens);

		var compacted = await CompactHistoryAsync(cancellationToken).ConfigureAwait(false);

		// After a successful compaction the projection is reset and the rebuilt session reports its real
		// size on the next turn, so only fail when we truly cannot make room.
		if (!compacted && projectedContextLengthTokens > _maxContextLengthTokens)
		{
			_logger.LogWarning(
				"Chat context limit exceeded and history could not be compacted. Effective={Effective}, Projected={Projected}, Max={Max}",
				effectiveContextLengthTokens,
				projectedContextLengthTokens,
				_maxContextLengthTokens);

			throw new ChatContextLimitExceededException(effectiveContextLengthTokens, _maxContextLengthTokens);
		}
	}

	/// <summary>
	/// Replaces the older portion of the transcript with a summary (or drops it, on trim/summarize failure)
	/// and rebuilds the chat session from the system prompt + compacted history + most recent turns.
	/// </summary>
	/// <returns><see langword="true"/> when history was compacted; <see langword="false"/> when there was nothing older to compact.</returns>
	private async Task<bool> CompactHistoryAsync(CancellationToken cancellationToken)
	{
		if (_transcript.Count <= _recentMessagesToKeep)
		{
			_logger.LogWarning(
				"History compaction skipped: {Count} message(s) present, nothing older than the retained {Keep}.",
				_transcript.Count,
				_recentMessagesToKeep);
			return false;
		}

		var olderCount = _transcript.Count - _recentMessagesToKeep;
		var older = _transcript.Take(olderCount).ToList();
		var recent = _transcript.Skip(olderCount).ToList();

		string? summary = null;
		if (_compactionStrategy == HistoryCompactionStrategy.Summarize)
		{
			try
			{
				summary = await _chatAdapter.SummarizeAsync(RenderTranscript(older), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "History summarization failed; falling back to trimming older turns.");
				summary = null;
			}
		}

		var summarized = !string.IsNullOrWhiteSpace(summary);
		var newHistory = new List<ChatMessage>();
		if (summarized)
		{
			newHistory.Add(new ChatMessage(ChatRole.User, $"[Earlier conversation summary]\n{summary}"));
			newHistory.Add(new ChatMessage(ChatRole.Model, "Understood, continuing from here."));
		}
		newHistory.AddRange(recent);

		_chatAdapter.RebuildSession(_currentSystemPrompt, _currentConfig.AllowedTools, newHistory);

		_transcript.Clear();
		_transcript.AddRange(newHistory);
		_projectedContextLengthTokens = null;

		_logger.LogInformation(
			"Compacted chat history from {Old} to {New} message(s) using {Strategy}.",
			older.Count + recent.Count,
			newHistory.Count,
			summarized ? "summary" : "trim");

		return true;
	}

	private static string RenderTranscript(IEnumerable<ChatMessage> messages) =>
		string.Join("\n\n", messages.Select(m => $"{(m.Role == ChatRole.Model ? "Assistant" : "User")}: {m.Text}"));

	private static int EstimateTokenCount(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return 0;
		}

		return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
	}

	private async Task<string> LoadContextAsync(ModeConfig config, CancellationToken cancellationToken)
	{
		if (_mcpAdapter == null || config.PreloadTools.Count == 0)
			return string.Empty;

		var sb = new StringBuilder();
		foreach (var tool in config.PreloadTools)
		{
			try
			{
				var result = await _mcpAdapter.CallToolAsync(tool, null, cancellationToken);
				sb.AppendLine($"### {tool}");
				sb.AppendLine(result);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Preload tool {Tool} failed during context load", tool);
			}
		}

		return sb.ToString();
	}

	private static string BuildSystemPrompt(ModeConfig config, string context)
	{
		if (string.IsNullOrEmpty(context))
			return config.SystemPrompt;

		return $"{config.SystemPrompt}\n\n## Current context\n\n{context}";
	}

	public async ValueTask DisposeAsync()
	{
		await _chatAdapter.DisposeAsync().ConfigureAwait(false);
	}
}
