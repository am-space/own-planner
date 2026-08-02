using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Factory implementation for creating IPlanningService instances with per-session MCP support
	/// </summary>
	public class ChatServiceFactory(
		IOptions<ChatSettings> settings,
		IChatAdapterFactory chatAdapterFactory,
		ILogger<ChatServiceFactory> logger,
		ILogger<DirectToolMcpAdapter> directToolMcpAdapterLogger,
		ILogger<PlanningService> planningServiceLogger,
		IServiceScopeFactory serviceScopeFactory,
		IPlannerSessionContextAccessor sessionContextAccessor,
		PerUserAppInitializationService initializationService)
		: IChatServiceFactory
	{
		private readonly ChatSettings _settings = settings.Value;

		public async Task<IPlanningService> CreateAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
		{
			logger.LogDebug("Creating new ChatServiceAdapter instance for session: {SessionId}, user: {UserId}", sessionId, userId);

			IMcpAdapter? mcpAdapter = null;
			try
			{
				logger.LogInformation("Initializing direct MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
				mcpAdapter = new DirectToolMcpAdapter(
					sessionId,
					userId,
					serviceScopeFactory,
					sessionContextAccessor,
					initializationService,
					directToolMcpAdapterLogger);
				await mcpAdapter.InitializeAsync(cancellationToken).ConfigureAwait(false);
				logger.LogInformation("Direct MCP adapter initialized successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to initialize direct MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);

				if (mcpAdapter != null)
				{
					try
					{
						await mcpAdapter.DisposeAsync().ConfigureAwait(false);
					}
					catch (Exception disposeEx)
					{
						logger.LogWarning(disposeEx, "Failed to dispose partially initialized MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
					}

					mcpAdapter = null;
				}
			}

			var chatAdapter = chatAdapterFactory.Create(mcpAdapter);

			logger.LogDebug("Chat adapter instance created successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
			return new PlanningService(chatAdapter, mcpAdapter, planningServiceLogger, _settings.Gemini.MaxContextLengthTokens);
		}
	}
}
