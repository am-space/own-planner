using OwnPlanner.Application.Chat;

namespace OwnPlanner.Web.Server.Services
{
	/// <summary>
	/// Factory for creating IPlanningService instances
	/// </summary>
	public interface IChatServiceFactory
	{
		/// <summary>
		/// Creates a new IPlanningService instance for a specific session
		/// </summary>
		/// <param name="sessionId">The session identifier to associate with the chat service</param>
		/// <param name="userId">The user identifier for database isolation</param>
		/// <param name="cancellationToken">Cancellation token</param>
		Task<IPlanningService> CreateAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
	}
}
