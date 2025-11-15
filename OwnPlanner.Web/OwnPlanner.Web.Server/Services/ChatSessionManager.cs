using System.Collections.Concurrent;
using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Manages chat sessions per login with conversation history preservation.
	/// Sessions are kept alive and renewed automatically when GetResponse is called.
	/// Only cleaned up after 30 minutes of inactivity.
	/// </summary>
	public class ChatSessionManager : IDisposable
	{
		private readonly IChatServiceFactory _factory;
		private readonly ILogger<ChatSessionManager> _logger;
		private readonly ConcurrentDictionary<string, ChatServiceAdapter> _sessions = new();
		private readonly Timer _cleanupTimer;
		private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);
		private bool _disposed;

		public ChatSessionManager(IChatServiceFactory factory, ILogger<ChatSessionManager> logger)
		{
			_factory = factory;
			_logger = logger;
			
			// Run cleanup every 5 minutes
			_cleanupTimer = new Timer(CleanupInactiveSessions, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
		}

		/// <summary>
		/// Gets or creates a chat session for the given session.
		/// Existing sessions are always reused to maintain conversation history.
		/// LastAccessTime is automatically updated by ChatServiceAdapter.GetResponse().
		/// </summary>
		/// <param name="sessionId">The unique session identifier from authentication cookie</param>
		/// <param name="userId">The user identifier for database isolation</param>
		/// <param name="cancellationToken">Cancellation token</param>
		public async Task<ChatServiceAdapter> GetOrCreateSessionAsync(string sessionId, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
			{
				throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
			}

			if (string.IsNullOrWhiteSpace(userId))
			{
				throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
			}

			// Check if session already exists
			if (_sessions.TryGetValue(sessionId, out var existingSession))
			{
				var sessionAge = DateTime.UtcNow - existingSession.CreatedTime;
				_logger.LogDebug("Reusing existing chat session: {SessionId} (age: {Age:F1}s, conversation history preserved)", 
					sessionId, sessionAge.TotalSeconds);
				return existingSession;
			}

			// No existing session - create a new one
			_logger.LogInformation("Creating new chat session: {SessionId} for user: {UserId}", sessionId, userId);
			var chatService = await _factory.CreateAsync(sessionId, userId, cancellationToken);

			_sessions[sessionId] = chatService;
			_logger.LogInformation("Chat session created: {SessionId}, Total sessions: {Count}", sessionId, _sessions.Count);
			
			return chatService;
		}

		/// <summary>
		/// Removes a specific session
		/// </summary>
		public async Task RemoveSessionAsync(string sessionId)
		{
			if (_sessions.TryRemove(sessionId, out var session))
			{
				_logger.LogInformation("Removing chat session: {SessionId}", sessionId);
				try
				{
					await session.DisposeAsync();
					_logger.LogDebug("Chat session disposed successfully: {SessionId}", sessionId);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error disposing chat session: {SessionId}", sessionId);
				}
			}
		}

		/// <summary>
		/// Gets the count of active sessions
		/// </summary>
		public int GetActiveSessionCount() => _sessions.Count;

		private void CleanupInactiveSessions(object? state)
		{
			try
			{
				var cutoffTime = DateTime.UtcNow - _sessionTimeout;
				var inactiveSessions = _sessions
					.Where(kvp => kvp.Value.LastAccessTime < cutoffTime)
					.ToList();

				if (inactiveSessions.Any())
				{
					_logger.LogInformation("Cleaning up {Count} inactive sessions (no activity for {Timeout} minutes)", 
						inactiveSessions.Count, _sessionTimeout.TotalMinutes);

					foreach (var (sessionId, session) in inactiveSessions)
					{
						if (_sessions.TryRemove(sessionId, out _))
						{
							var sessionAge = DateTime.UtcNow - session.CreatedTime;
							var inactiveDuration = DateTime.UtcNow - session.LastAccessTime;
							
							_logger.LogDebug("Removed inactive session: {SessionId} (age: {Age:F1}min, inactive: {Inactive:F1}min)", 
								sessionId, sessionAge.TotalMinutes, inactiveDuration.TotalMinutes);
							
							// Fire and forget disposal
							_ = Task.Run(async () =>
							{
								try
								{
									await session.DisposeAsync();
								}
								catch (Exception ex)
								{
									_logger.LogError(ex, "Error disposing session: {SessionId}", sessionId);
								}
							});
						}
					}

					_logger.LogInformation("Cleanup complete. Remaining sessions: {Count}", _sessions.Count);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error during session cleanup");
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			_cleanupTimer?.Dispose();

			// Dispose all active sessions
			foreach (var session in _sessions.Values)
			{
				try
				{
					session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error disposing chat session during cleanup");
				}
			}

			_sessions.Clear();
			_logger.LogInformation("ChatSessionManager disposed");
		}
	}
}
