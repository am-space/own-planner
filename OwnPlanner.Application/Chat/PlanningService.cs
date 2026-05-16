using System.Text;
using Microsoft.Extensions.Logging;

namespace OwnPlanner.Application.Chat;

public sealed class PlanningService(IChatAdapter chatAdapter, IMcpAdapter? mcpAdapter, ILogger<PlanningService> logger, int maxContextLengthTokens = 64 * 1024) : IPlanningService
{
	private readonly IChatAdapter _chatAdapter = chatAdapter;
	private readonly IMcpAdapter? _mcpAdapter = mcpAdapter;
	private readonly ILogger<PlanningService> _logger = logger;
   private readonly int _maxContextLengthTokens = maxContextLengthTokens;
	private PlanningMode _currentMode = PlanningMode.DayWork;
	private ModeConfig _currentConfig = ModeConfig.All[PlanningMode.DayWork];
	private bool _modeActivated;

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
		_modeActivated = true;

		_logger.LogInformation("Planning mode switched to {Mode}", mode);
	}

   public async Task<ChatTurnResult> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
	{
		if (!_modeActivated)
		{
			await SwitchModeAsync(_currentMode, cancellationToken).ConfigureAwait(false);
		}

		string message = userMessage;

		if (_modeActivated && _currentConfig.RefreshOnTurn && _mcpAdapter != null)
		{
			var context = await LoadContextAsync(_currentConfig, cancellationToken);
			if (!string.IsNullOrEmpty(context))
				message = $"[Refreshed context]\n{context}\n\n[User message]\n{userMessage}";
		}

		EnsureContextWithinLimit(message);

		return await _chatAdapter.GetResponse(message);
	}

	private void EnsureContextWithinLimit(string message)
	{
		var currentContextLengthTokens = _chatAdapter.CurrentContextLengthTokens;
		var estimatedMessageTokens = EstimateTokenCount(message);
		var projectedContextLengthTokens = (currentContextLengthTokens ?? 0) + estimatedMessageTokens;

		if (currentContextLengthTokens.GetValueOrDefault() >= _maxContextLengthTokens || projectedContextLengthTokens > _maxContextLengthTokens)
		{
			_logger.LogWarning(
				"Chat context limit exceeded. CurrentContextLengthTokens={CurrentContextLengthTokens}, EstimatedMessageTokens={EstimatedMessageTokens}, MaxContextLengthTokens={MaxContextLengthTokens}",
				currentContextLengthTokens,
				estimatedMessageTokens,
				_maxContextLengthTokens);

			throw new ChatContextLimitExceededException(currentContextLengthTokens, _maxContextLengthTokens);
		}
	}

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
