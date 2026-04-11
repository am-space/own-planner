using OwnPlanner.Application.Chat;

namespace OwnPlanner.Web.Server.Services
{
	public interface IChatSessionManager
	{
		Task<IPlanningService> GetOrCreateSessionAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
      IPlanningService? GetSession(string sessionId);
		Task RemoveSessionAsync(string sessionId);
		int GetActiveSessionCount();
	}
}
