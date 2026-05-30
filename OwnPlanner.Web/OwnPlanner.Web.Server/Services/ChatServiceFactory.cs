using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Factory implementation for creating IPlanningService instances with per-session MCP support
	/// </summary>
	public class ChatServiceFactory : IChatServiceFactory
	{
		private readonly ChatSettings _settings;
		private readonly ILogger<ChatServiceFactory> _logger;
		private readonly ILogger<DirectToolMcpAdapter> _directToolMcpAdapterLogger;
		private readonly ILogger<PlanningService> _planningServiceLogger;
		private readonly IServiceScopeFactory _serviceScopeFactory;
		private readonly IPlannerSessionContextAccessor _sessionContextAccessor;
		private readonly PerUserAppInitializationService _initializationService;

		public ChatServiceFactory(
			IOptions<ChatSettings> settings,
			ILogger<ChatServiceFactory> logger,
			ILogger<DirectToolMcpAdapter> directToolMcpAdapterLogger,
			ILogger<PlanningService> planningServiceLogger,
			IServiceScopeFactory serviceScopeFactory,
			IPlannerSessionContextAccessor sessionContextAccessor,
			PerUserAppInitializationService initializationService)
		{
			_settings = settings.Value;
			_logger = logger;
			_directToolMcpAdapterLogger = directToolMcpAdapterLogger;
			_planningServiceLogger = planningServiceLogger;
			_serviceScopeFactory = serviceScopeFactory;
			_sessionContextAccessor = sessionContextAccessor;
			_initializationService = initializationService;
		}

		public async Task<IPlanningService> CreateAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
		{
			_logger.LogDebug("Creating new ChatServiceAdapter instance for session: {SessionId}, user: {UserId}", sessionId, userId);

			IMcpAdapter? mcpAdapter = null;
			try
			{
				_logger.LogInformation("Initializing direct MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
				mcpAdapter = new DirectToolMcpAdapter(
					sessionId,
					userId,
					_serviceScopeFactory,
					_sessionContextAccessor,
					_initializationService,
					_directToolMcpAdapterLogger);
				await mcpAdapter.InitializeAsync(cancellationToken).ConfigureAwait(false);
				_logger.LogInformation("Direct MCP adapter initialized successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to initialize direct MCP adapter for session: {SessionId}, user: {UserId}", sessionId, userId);
			}

			var chatService = new ChatServiceAdapter(
				_settings.Gemini.ApiKey,
				_settings.Gemini.Model,
				_settings.Gemini.MaxToolCallRounds,
				mcpAdapter
			);

			_logger.LogDebug("ChatServiceAdapter instance created successfully for session: {SessionId}, user: {UserId}", sessionId, userId);
            return new PlanningService(chatService, mcpAdapter, _planningServiceLogger, _settings.Gemini.MaxContextLengthTokens);
		}
	}
}
