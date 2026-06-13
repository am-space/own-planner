using System.Text;
using Microsoft.Extensions.Logging;

namespace OwnPlanner.Application.Chat;

public sealed class PlanningService(IChatAdapter chatAdapter, IMcpAdapter? mcpAdapter, ILogger<PlanningService> logger, int maxContextLengthTokens = 64 * 1024) : IPlanningService
{
	private readonly IChatAdapter _chatAdapter = chatAdapter;
	private readonly IMcpAdapter? _mcpAdapter = mcpAdapter;
	private readonly ILogger<PlanningService> _logger = logger;
	private readonly int _maxContextLengthTokens = maxContextLengthTokens > 0
		? maxContextLengthTokens
		: throw new ArgumentOutOfRangeException(nameof(maxContextLengthTokens), "maxContextLengthTokens must be greater than 0.");
	private PlanningMode _currentMode = PlanningMode.DayWork;
	private ModeConfig _currentConfig = ModeConfig.All[PlanningMode.DayWork];
	private bool _modeActivated;
	// PromptTokenCount reflects prompt-side usage only; keep a local next-turn projection that also includes assistant output.
	private int? _projectedContextLengthTokens;

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
		_projectedContextLengthTokens = null;

		_logger.LogInformation("Planning mode switched to {Mode}", mode);
	}

   public async Task<ChatTurnResult> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
	{
		if (!_modeActivated)
		{
			await SwitchModeAsync(_currentMode, cancellationToken).ConfigureAwait(false);
		}

		EnsureContextWithinLimit(userMessage);

		var result = await _chatAdapter.GetResponse(userMessage);
		var assistantResponseTokens = EstimateTokenCount(result.Message);
		_projectedContextLengthTokens = result.ContextLengthTokens is int promptTokens
			? promptTokens + assistantResponseTokens
			: null;

		return result;
	}

	private void EnsureContextWithinLimit(string message)
	{
		var currentContextLengthTokens = _chatAdapter.CurrentContextLengthTokens;
		var effectiveContextLengthTokens = Math.Max(currentContextLengthTokens ?? 0, _projectedContextLengthTokens ?? 0);
		var estimatedMessageTokens = EstimateTokenCount(message);
		var projectedContextLengthTokens = effectiveContextLengthTokens + estimatedMessageTokens;

		if (effectiveContextLengthTokens >= _maxContextLengthTokens || projectedContextLengthTokens > _maxContextLengthTokens)
		{
			_logger.LogWarning(
				"Chat context limit exceeded. CurrentContextLengthTokens={CurrentContextLengthTokens}, ProjectedContextLengthTokens={ProjectedContextLengthTokens}, EstimatedMessageTokens={EstimatedMessageTokens}, MaxContextLengthTokens={MaxContextLengthTokens}",
				currentContextLengthTokens,
				_projectedContextLengthTokens,
				estimatedMessageTokens,
				_maxContextLengthTokens);

			throw new ChatContextLimitExceededException(effectiveContextLengthTokens, _maxContextLengthTokens);
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
