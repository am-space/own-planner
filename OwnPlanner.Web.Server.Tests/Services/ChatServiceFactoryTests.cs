using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OwnPlanner.Application.Chat;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class ChatServiceFactoryTests
{
	private readonly IChatAdapterFactory _chatAdapterFactory = Substitute.For<IChatAdapterFactory>();
	private readonly IChatAdapter _chatAdapter = Substitute.For<IChatAdapter>();
	private readonly IServiceScopeFactory _serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
	private readonly IPlannerSessionContextAccessor _sessionContextAccessor = Substitute.For<IPlannerSessionContextAccessor>();
	private readonly PerUserAppInitializationService _initializationService;
	private readonly ChatServiceFactory _factory;

	public ChatServiceFactoryTests()
	{
		var settings = Options.Create(new ChatSettings
		{
			Gemini = new GeminiSettings { MaxContextLengthTokens = 42_000 }
		});
		_initializationService = new PerUserAppInitializationService(
			_serviceScopeFactory,
			_sessionContextAccessor,
			Substitute.For<ILogger<PerUserAppInitializationService>>());
		_chatAdapterFactory.Create(Arg.Any<IMcpAdapter?>()).Returns(_chatAdapter);
		_chatAdapter.DisposeAsync().Returns(ValueTask.CompletedTask);

		_factory = new ChatServiceFactory(
			settings,
			_chatAdapterFactory,
			Substitute.For<ILogger<ChatServiceFactory>>(),
			Substitute.For<ILogger<DirectToolMcpAdapter>>(),
			Substitute.For<ILogger<PlanningService>>(),
			_serviceScopeFactory,
			_sessionContextAccessor,
			_initializationService);
	}

	[Fact]
	public async Task CreateAsync_PassesInitializedDirectToolAdapterToChatAdapterFactory()
	{
		var ct = TestContext.Current.CancellationToken;
		IMcpAdapter? capturedMcpAdapter = null;
		_chatAdapterFactory.Create(Arg.Do<IMcpAdapter?>(adapter => capturedMcpAdapter = adapter))
			.Returns(_chatAdapter);

		var planningService = await _factory.CreateAsync("session-123", "user-456", ct);

		capturedMcpAdapter.Should().BeOfType<DirectToolMcpAdapter>();
		(await capturedMcpAdapter!.ListToolDetailsAsync(ct)).Should().NotBeEmpty();
		planningService.MaxContextLengthTokens.Should().Be(42_000);
		await planningService.DisposeAsync();
	}

	[Fact]
	public async Task CreateAsync_ResultOwnsChatAdapterLifetime()
	{
		var ct = TestContext.Current.CancellationToken;
		var planningService = await _factory.CreateAsync("session-123", "user-456", ct);

		await planningService.DisposeAsync();

		await _chatAdapter.Received(1).DisposeAsync();
	}
}
