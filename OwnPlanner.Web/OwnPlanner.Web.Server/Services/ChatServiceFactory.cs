using Microsoft.Extensions.Options;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Factory implementation for creating ChatServiceAdapter instances with per-session MCP support
	/// </summary>
	public class ChatServiceFactory : IChatServiceFactory
	{
		private readonly ChatSettings _settings;
		private readonly ILogger<ChatServiceFactory> _logger;

		public ChatServiceFactory(IOptions<ChatSettings> settings, ILogger<ChatServiceFactory> logger)
		{
			_settings = settings.Value;
			_logger = logger;
		}

		public async Task<ChatServiceAdapter> CreateAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
		{
			_logger.LogDebug("Creating new ChatServiceAdapter instance for session: {SessionId}, user: {UserId}", sessionId, userId);

			// Create a dedicated MCP adapter for this session if configured
			McpAdapter? mcpAdapter = null;
			if (!string.IsNullOrEmpty(_settings.Mcp.Command))
			{
				try
				{
					_logger.LogInformation("Initializing MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
					
					// Add session ID and user ID as arguments to the MCP server
					var mcpArguments = _settings.Mcp.Arguments.ToList();
					mcpArguments.Add("--session-id");
					mcpArguments.Add(sessionId);
					mcpArguments.Add("--user-id");
					mcpArguments.Add(userId);
					
					mcpAdapter = new McpAdapter(_settings.Mcp.Command, mcpArguments.ToArray());
					await mcpAdapter.InitializeAsync(cancellationToken);
					
					_logger.LogInformation("MCP adapter initialized successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to initialize MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
					// Continue without MCP
				}
			}

			var chatService = new ChatServiceAdapter(
				_settings.Gemini.ApiKey,
				_settings.Gemini.Model,
				_settings.Gemini.MaxToolCallRounds,
				mcpAdapter
			);

			_logger.LogDebug("ChatServiceAdapter instance created successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
			return chatService;
		}
	}
}
