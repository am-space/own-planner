using System.Text;
using Microsoft.Extensions.Logging;

namespace OwnPlanner.Application.Chat;

public sealed class PlanningService(IChatAdapter chatAdapter, IMcpAdapter? mcpAdapter, ILogger<PlanningService> logger) : IPlanningService
{
	private readonly IChatAdapter _chatAdapter = chatAdapter;
	private readonly IMcpAdapter? _mcpAdapter = mcpAdapter;
	private readonly ILogger<PlanningService> _logger = logger;
	private PlanningMode _currentMode = PlanningMode.DayWork;
	private ModeConfig _currentConfig = ModeConfig.All[PlanningMode.DayWork];
	private bool _modeActivated;

	public DateTime CreatedTime => _chatAdapter.CreatedTime;
	public DateTime LastAccessTime => _chatAdapter.LastAccessTime;
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

	public async Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
	{
		string message = userMessage;

		if (_modeActivated && _currentConfig.RefreshOnTurn && _mcpAdapter != null)
		{
			var context = await LoadContextAsync(_currentConfig, cancellationToken);
			if (!string.IsNullOrEmpty(context))
				message = $"[Refreshed context]\n{context}\n\n[User message]\n{userMessage}";
		}

		return await _chatAdapter.GetResponse(message);
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
