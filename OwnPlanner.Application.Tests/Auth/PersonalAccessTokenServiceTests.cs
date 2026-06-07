using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OwnPlanner.Application.Auth;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Tests.Auth;

public sealed class PersonalAccessTokenServiceTests
{
	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
	private readonly IPersonalAccessTokenRepository _tokenRepository = Substitute.For<IPersonalAccessTokenRepository>();
	private readonly AuthService _service;

	public PersonalAccessTokenServiceTests()
	{
		_service = new AuthService(_userRepository, _tokenRepository, NullLogger<AuthService>.Instance);
	}

	[Fact]
	public async Task CreatePersonalAccessTokenAsync_CreatesTokenAndReturnsPlaintext()
	{
		var ct = TestContext.Current.CancellationToken;
		PersonalAccessToken? captured = null;
		_tokenRepository.AddAsync(Arg.Do<PersonalAccessToken>(token => captured = token), ct)
			.Returns(callInfo => Task.FromResult(callInfo.Arg<PersonalAccessToken>()));

		var result = await _service.CreatePersonalAccessTokenAsync(
			Guid.NewGuid(),
			new CreatePersonalAccessTokenRequest("Claude Code"),
			ct);

		result.PlaintextToken.Should().StartWith("opat_");
		result.Token.Name.Should().Be("Claude Code");
		result.Token.RevokedAt.Should().BeNull();
		captured.Should().NotBeNull();
		captured!.Name.Should().Be("Claude Code");
		captured.TokenHash.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public async Task ListPersonalAccessTokensAsync_ReturnsTokensOrderedByNewestFirst()
	{
		var ct = TestContext.Current.CancellationToken;
		var older = new PersonalAccessToken(Guid.NewGuid(), "Old", "hash-old");
		await Task.Delay(5, ct);
		var newer = new PersonalAccessToken(Guid.NewGuid(), "New", "hash-new");
		newer.RecordUsage();

		_tokenRepository.ListByUserIdAsync(Arg.Any<Guid>(), ct).Returns([older, newer]);

		var tokens = await _service.ListPersonalAccessTokensAsync(Guid.NewGuid(), ct);

		tokens.Should().HaveCount(2);
		tokens.First().Name.Should().Be("New");
	}

	[Fact]
	public async Task RevokePersonalAccessTokenAsync_ReturnsFalse_WhenTokenMissing()
	{
		var ct = TestContext.Current.CancellationToken;
		_tokenRepository.GetByIdAsync(Arg.Any<Guid>(), ct).Returns((PersonalAccessToken?)null);

		var revoked = await _service.RevokePersonalAccessTokenAsync(Guid.NewGuid(), Guid.NewGuid(), ct);

		revoked.Should().BeFalse();
	}

	[Fact]
	public async Task ResolveMcpBearerTokenUserIdAsync_ReturnsUserIdAndTouchesToken()
	{
		var ct = TestContext.Current.CancellationToken;
		var userId = Guid.NewGuid();
		var token = new PersonalAccessToken(userId, "Claude Code", "hash");
		_tokenRepository.FindActiveByTokenHashAsync(Arg.Any<string>(), ct).Returns(token);
		_tokenRepository.UpdateAsync(token, ct).Returns(Task.FromResult(token));

		var resolvedUserId = await _service.ResolveMcpBearerTokenUserIdAsync("raw-token", ct);

		resolvedUserId.Should().Be(userId.ToString());
		token.LastUsedAt.Should().NotBeNull();
		await _tokenRepository.Received(1).UpdateAsync(token, ct);
	}
}
