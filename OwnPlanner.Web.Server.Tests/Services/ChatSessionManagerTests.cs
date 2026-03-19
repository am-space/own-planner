using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Chat;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public class ChatSessionManagerTests : IDisposable
{
	private readonly IChatServiceFactory _factory = Substitute.For<IChatServiceFactory>();
	private readonly ILogger<ChatSessionManager> _logger = Substitute.For<ILogger<ChatSessionManager>>();
	private readonly ChatSessionManager _manager;

	public ChatSessionManagerTests()
	{
		_manager = new ChatSessionManager(_factory, _logger);
	}

	private static IPlanningService CreateSession(DateTime lastAccessTime)
	{
		var session = Substitute.For<IPlanningService>();
		session.LastAccessTime.Returns(lastAccessTime);
		session.CreatedTime.Returns(lastAccessTime);
		session.DisposeAsync().Returns(ValueTask.CompletedTask);
		return session;
	}

	private async Task AddSessionAsync(string sessionId, IPlanningService session)
	{
		_factory.CreateAsync(sessionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(session);
		await _manager.GetOrCreateSessionAsync(sessionId, "user-1");
	}

	// --- CleanupInactiveSessions ---

	[Fact]
	public async Task CleanupInactiveSessions_NoInactiveSessions_DoesNotRemoveAny()
	{
		var activeSession = CreateSession(DateTime.UtcNow);
		await AddSessionAsync("active-session", activeSession);

		await _manager.CleanupInactiveSessionsAsync();

		_manager.GetActiveSessionCount().Should().Be(1);
	}

	[Fact]
	public async Task CleanupInactiveSessions_InactiveSession_IsRemoved()
	{
		var inactiveSession = CreateSession(DateTime.UtcNow.AddMinutes(-31));
		await AddSessionAsync("inactive-session", inactiveSession);

		await _manager.CleanupInactiveSessionsAsync();

		_manager.GetActiveSessionCount().Should().Be(0);
	}

	[Fact]
	public async Task CleanupInactiveSessions_MixedSessions_OnlyInactiveIsRemoved()
	{
		var activeSession = CreateSession(DateTime.UtcNow);
		var inactiveSession = CreateSession(DateTime.UtcNow.AddMinutes(-31));
		await AddSessionAsync("active-session", activeSession);
		await AddSessionAsync("inactive-session", inactiveSession);

		await _manager.CleanupInactiveSessionsAsync();

		_manager.GetActiveSessionCount().Should().Be(1);
	}

	[Fact]
	public async Task CleanupInactiveSessions_InactiveSession_CallsDisposeAsync()
	{
		var inactiveSession = CreateSession(DateTime.UtcNow.AddMinutes(-31));
		await AddSessionAsync("inactive-session", inactiveSession);

		await _manager.CleanupInactiveSessionsAsync();

		await inactiveSession.Received(1).DisposeAsync();
	}

	[Fact]
	public async Task CleanupInactiveSessions_ActiveSession_DoesNotCallDisposeAsync()
	{
		var activeSession = CreateSession(DateTime.UtcNow);
		await AddSessionAsync("active-session", activeSession);

		await _manager.CleanupInactiveSessionsAsync();

		await activeSession.DidNotReceive().DisposeAsync();
	}

	[Fact]
	public async Task CleanupInactiveSessions_SessionWithinTimeout_IsNotRemoved()
	{
		var recentSession = CreateSession(DateTime.UtcNow.AddMinutes(-29));
		await AddSessionAsync("recent-session", recentSession);

		await _manager.CleanupInactiveSessionsAsync();

		_manager.GetActiveSessionCount().Should().Be(1);
	}

	public void Dispose() => _manager.Dispose();
}
