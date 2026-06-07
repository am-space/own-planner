using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OwnPlanner.Application.Auth;
using OwnPlanner.Web.Server.Authentication;

namespace OwnPlanner.Web.Server.Tests.Authentication;

public sealed class McpBearerAuthenticationHandlerTests
{
	[Fact]
	public async Task AuthenticateAsync_ReturnsNoResult_WhenAuthorizationHeaderIsMissing()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: null,
			authService: Substitute.For<IAuthService>());

		result.None.Should().BeTrue();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsNoResult_WhenSchemeIsNotBearer()
	{
		var result = await AuthenticateAsync(
			authorizationHeader: "Basic abc123",
			authService: Substitute.For<IAuthService>());

		result.None.Should().BeTrue();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsFail_WhenBearerTokenIsInvalid()
	{
		var authService = Substitute.For<IAuthService>();
		authService.ResolveMcpBearerTokenUserIdAsync("invalid-token", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>(null));

		var result = await AuthenticateAsync(
			authorizationHeader: "Bearer invalid-token",
			authService);

		result.Succeeded.Should().BeFalse();
		result.Failure.Should().NotBeNull();
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsPrincipal_WhenBearerTokenIsValid()
	{
		var authService = Substitute.For<IAuthService>();
		authService.ResolveMcpBearerTokenUserIdAsync("valid-token", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>("user-42"));

		var result = await AuthenticateAsync(
			authorizationHeader: "Bearer valid-token",
			authService);

		result.Succeeded.Should().BeTrue();
		result.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be("user-42");
	}

	[Fact]
	public async Task AuthenticateAsync_ReturnsFail_WhenResolvedUserIdIsWhitespace()
	{
		var authService = Substitute.For<IAuthService>();
		authService.ResolveMcpBearerTokenUserIdAsync("valid-token", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>("   "));

		var result = await AuthenticateAsync(
			authorizationHeader: "Bearer valid-token",
			authService);

		result.Succeeded.Should().BeFalse();
		result.Failure.Should().NotBeNull();
	}

	[Fact]
	public async Task ChallengeAsync_AddsBearerWwwAuthenticateHeader()
	{
		var context = new DefaultHttpContext();
		var handler = new McpBearerAuthenticationHandler(
			new TestAuthenticationOptionsMonitor(),
			NullLoggerFactory.Instance,
			UrlEncoder.Default,
			Substitute.For<IAuthService>());

		var scheme = new AuthenticationScheme(
			McpBearerAuthenticationDefaults.AuthenticationScheme,
			displayName: null,
			typeof(McpBearerAuthenticationHandler));

		await handler.InitializeAsync(scheme, context);
		await handler.ChallengeAsync(new AuthenticationProperties());

		context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Bearer");
	}

	private static async Task<AuthenticateResult> AuthenticateAsync(string? authorizationHeader, IAuthService authService)
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
			authService);

		var scheme = new AuthenticationScheme(
			McpBearerAuthenticationDefaults.AuthenticationScheme,
			displayName: null,
			typeof(McpBearerAuthenticationHandler));

		await handler.InitializeAsync(scheme, context);
		return await handler.AuthenticateAsync();
	}

	private sealed class TestAuthenticationOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
	{
		public AuthenticationSchemeOptions CurrentValue => new();

		public AuthenticationSchemeOptions Get(string? name) => new();

		public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
	}
}
