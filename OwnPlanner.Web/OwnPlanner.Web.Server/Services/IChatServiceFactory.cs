using OwnPlanner.Infrastructure.Adapters;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Factory for creating ChatServiceAdapter instances
	/// </summary>
	public interface IChatServiceFactory
	{
		/// <summary>
		/// Creates a new ChatServiceAdapter instance for a specific session
		/// </summary>
		/// <param name="sessionId">The session identifier to associate with the chat service</param>
		/// <param name="userId">The user identifier for database isolation</param>
		/// <param name="cancellationToken">Cancellation token</param>
		Task<ChatServiceAdapter> CreateAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
	}
}
