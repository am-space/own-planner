using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OwnPlanner.Web.Server.Authentication;

namespace OwnPlanner.Web.Server.Tests.Authentication;

public sealed class McpBearerAuthenticationHandlerTests
{
	[Fact]
	public async Task AuthenticateAsync_ReturnsNoResult_WhenAuthorizationHeaderIsMissing()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: null,
			resolver: new DictionaryResolver(new Dictionary<string, string>(StringComparer.Ordinal)));

		result.None.Should().BeTrue();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsNoResult_WhenSchemeIsNotBearer()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: "Basic abc123",
			resolver: new DictionaryResolver(new Dictionary<string, string>(StringComparer.Ordinal)));

		result.None.Should().BeTrue();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsFail_WhenBearerTokenIsInvalid()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: "Bearer invalid-token",
			resolver: new DictionaryResolver(new Dictionary<string, string>(StringComparer.Ordinal)));

		result.Succeeded.Should().BeFalse();
		result.Failure.Should().NotBeNull();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsPrincipal_WhenBearerTokenIsValid()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: "Bearer valid-token",
			resolver: new DictionaryResolver(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["valid-token"] = "user-42"
			}));

		result.Succeeded.Should().BeTrue();
		result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be("user-42");
	}

	private static async Task<AuthenticateResult> AuthenticateAsync(string? authorizationHeader, IMcpBearerTokenResolver resolver)
	{
		var context = new DefaultHttpContext();
		if (!string.IsNullOrWhiteSpace(authorizationHeader))
		{
			context.Request.Headers.Authorization = authorizationHeader;
		}

		var handler = new McpBearerAuthenticationHandler(
			new TestAuthenticationOptionsMonitor(),
			NullLoggerFactory.Instance,
			UrlEncoder.Default,
			resolver);

		var scheme = new AuthenticationScheme(
			McpBearerAuthenticationDefaults.AuthenticationScheme,
			displayName: null,
			typeof(McpBearerAuthenticationHandler));

		await handler.InitializeAsync(scheme, context);
		return await handler.AuthenticateAsync();
	}

	private sealed class DictionaryResolver(IReadOnlyDictionary<string, string> tokenToUserId) : IMcpBearerTokenResolver
	{
		public bool TryResolveUserId(string token, out string userId)
		{
			return tokenToUserId.TryGetValue(token.Trim(), out userId!);
		}
	}

	private sealed class TestAuthenticationOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
	{
		public AuthenticationSchemeOptions CurrentValue => new();

		public AuthenticationSchemeOptions Get(string? name) => new();

		public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
	}
}
