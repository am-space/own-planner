using FluentAssertions;
using Microsoft.Extensions.Options;
using OwnPlanner.Web.Server.Authentication;
using OwnPlanner.Web.Server.Configuration;

namespace OwnPlanner.Web.Server.Tests.Authentication;

public sealed class ConfigurationMcpBearerTokenResolverTests
{
	[Fact]
	public void TryResolveUserId_ReturnsMappedUserId_WhenTokenExists()
	{
		var settings = Options.Create(new McpBearerSettings
		{
			TokenBindings =
			[
				new McpBearerTokenBinding
				{
					Token = "token-1",
					UserId = "user-a"
				}
			]
		});
		var resolver = new ConfigurationMcpBearerTokenResolver(settings);

		var found = resolver.TryResolveUserId("token-1", out var userId);

		found.Should().BeTrue();
		userId.Should().Be("user-a");
	}

	[Fact]
	public void TryResolveUserId_ReturnsFalse_WhenTokenDoesNotExist()
	{
		var settings = Options.Create(new McpBearerSettings
		{
			TokenBindings =
			[
				new McpBearerTokenBinding
				{
					Token = "token-1",
					UserId = "user-a"
				}
			]
		});
		var resolver = new ConfigurationMcpBearerTokenResolver(settings);

		var found = resolver.TryResolveUserId("token-2", out _);

		found.Should().BeFalse();
	}

	[Fact]
	public void TryResolveUserId_DoesNotThrowAndSkipsBinding_WhenTokenIsNull()
	{
		var settings = Options.Create(new McpBearerSettings
		{
			TokenBindings =
			[
				new McpBearerTokenBinding
				{
					Token = null!,
					UserId = "user-a"
				}
			]
		});
		var resolver = new ConfigurationMcpBearerTokenResolver(settings);

		var found = resolver.TryResolveUserId("token-1", out _);

		found.Should().BeFalse();
	}

	[Fact]
	public void TryResolveUserId_ReturnsFalse_WhenMappedUserIdIsNull()
	{
		var settings = Options.Create(new McpBearerSettings
		{
			TokenBindings =
			[
				new McpBearerTokenBinding
				{
					Token = "token-1",
					UserId = null!
				}
			]
		});
		var resolver = new ConfigurationMcpBearerTokenResolver(settings);

		var found = resolver.TryResolveUserId("token-1", out _);

		found.Should().BeFalse();
	}

	[Fact]
	public void TryResolveUserId_ReturnsFalse_WhenMappedUserIdIsWhitespace()
	{
		var settings = Options.Create(new McpBearerSettings
		{
			TokenBindings =
			[
				new McpBearerTokenBinding
				{
					Token = "token-1",
					UserId = "   "
				}
			]
		});
		var resolver = new ConfigurationMcpBearerTokenResolver(settings);

		var found = resolver.TryResolveUserId("token-1", out _);

		found.Should().BeFalse();
	}
}
