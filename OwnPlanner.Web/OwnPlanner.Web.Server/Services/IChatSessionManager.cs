using OwnPlanner.Application.Chat;

namespace OwnPlanner.Web.Server.Services
{
	public interface IChatSessionManager
	{
		Task<IPlanningService> GetOrCreateSessionAsync(string sessionId, string userId, CancellationToken cancellationToken = default);
		Task RemoveSessionAsync(string sessionId);
		int GetActiveSessionCount();
	}
}
