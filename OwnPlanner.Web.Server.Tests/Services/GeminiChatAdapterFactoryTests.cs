using FluentAssertions;
using Microsoft.Extensions.Options;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Services;

public sealed class GeminiChatAdapterFactoryTests
{
	[Fact]
	public async Task Create_ReturnsProductionChatServiceAdapter()
	{
		var settings = Options.Create(new ChatSettings
		{
			Gemini = new GeminiSettings
			{
				ApiKey = "AIza" + new string('x', 35),
				Model = "test-model",
				MaxToolCallRounds = 3,
			}
		});
		var factory = new GeminiChatAdapterFactory(settings);

		var adapter = factory.Create(mcpAdapter: null);

		adapter.Should().BeOfType<ChatServiceAdapter>();
		await adapter.DisposeAsync();
	}
}
