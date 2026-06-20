using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OwnPlanner.Application.Auth;
using OwnPlanner.Application.Email;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Tests.Auth;

public sealed class PasswordResetServiceTests
{
	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
	private readonly IPersonalAccessTokenRepository _tokenRepository = Substitute.For<IPersonalAccessTokenRepository>();
	private readonly IPasswordResetTokenRepository _resetTokenRepository = Substitute.For<IPasswordResetTokenRepository>();
	private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
	private readonly AuthService _service;

	public PasswordResetServiceTests()
	{
		_service = new AuthService(
			_userRepository,
			_tokenRepository,
			_resetTokenRepository,
			_emailSender,
			new EmailOptions { ResetUrlBase = "https://controlcode.space", ResetTokenLifetimeMinutes = 30 },
			NullLogger<AuthService>.Instance);
	}

	[Fact]
	public async Task RequestPasswordResetAsync_StoresHashedTokenAndSendsEmail_WhenUserExists()
	{
		var ct = TestContext.Current.CancellationToken;
		var user = new User("user@example.com", "tester", "existing-hash");
		_userRepository.GetByEmailAsync("user@example.com", ct).Returns(user);

		PasswordResetToken? captured = null;
		_resetTokenRepository.AddAsync(Arg.Do<PasswordResetToken>(t => captured = t), ct)
			.Returns(callInfo => Task.FromResult(callInfo.Arg<PasswordResetToken>()));

		await _service.RequestPasswordResetAsync("user@example.com", ct);

		captured.Should().NotBeNull();
		captured!.UserId.Should().Be(user.Id);
		captured.TokenHash.Should().NotBeNullOrWhiteSpace();
		captured.ConsumedAt.Should().BeNull();
		await _resetTokenRepository.Received(1).InvalidateActiveForUserAsync(user.Id, ct);
		await _emailSender.Received(1).SendAsync(user.Email, Arg.Any<string>(), Arg.Any<string>(), ct);
	}

	[Fact]
	public async Task RequestPasswordResetAsync_DoesNothing_WhenUserDoesNotExist()
	{
		var ct = TestContext.Current.CancellationToken;
		_userRepository.GetByEmailAsync(Arg.Any<string>(), ct).Returns((User?)null);

		await _service.RequestPasswordResetAsync("missing@example.com", ct);

		await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<PasswordResetToken>(), ct);
		await _emailSender.DidNotReceive().SendAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_UpdatesPasswordAndConsumesToken_WhenTokenValid()
	{
		var ct = TestContext.Current.CancellationToken;
		const string plaintext = "oprt_deadbeef";
		var user = new User("user@example.com", "tester", "old-hash");
		var resetToken = new PasswordResetToken(user.Id, HashToken(plaintext), DateTime.UtcNow.AddMinutes(15));

		_resetTokenRepository.FindActiveByTokenHashAsync(HashToken(plaintext), ct).Returns(resetToken);
		_userRepository.GetByIdAsync(user.Id, ct).Returns(user);

		var result = await _service.ResetPasswordAsync(plaintext, "new-password-123", ct);

		result.Success.Should().BeTrue();
		_service.VerifyPassword("new-password-123", user.PasswordHash).Should().BeTrue();
		resetToken.ConsumedAt.Should().NotBeNull();
		await _userRepository.Received(1).UpdateAsync(user, ct);
		await _resetTokenRepository.Received(1).UpdateAsync(resetToken, ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenTokenUnknown()
	{
		var ct = TestContext.Current.CancellationToken;
		_resetTokenRepository.FindActiveByTokenHashAsync(Arg.Any<string>(), ct).Returns((PasswordResetToken?)null);

		var result = await _service.ResetPasswordAsync("oprt_unknown", "new-password-123", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Invalid or expired reset token");
		await _userRepository.DidNotReceive().UpdateAsync(Arg.Any<User>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenTokenExpired()
	{
		var ct = TestContext.Current.CancellationToken;
		const string plaintext = "oprt_expired";
		var user = new User("user@example.com", "tester", "old-hash");
		var expiredToken = new PasswordResetToken(user.Id, HashToken(plaintext), DateTime.UtcNow.AddMinutes(-1));

		// Even if the store returned it, the service must reject an expired token.
		_resetTokenRepository.FindActiveByTokenHashAsync(HashToken(plaintext), ct).Returns(expiredToken);

		var result = await _service.ResetPasswordAsync(plaintext, "new-password-123", ct);

		result.Success.Should().BeFalse();
		await _userRepository.DidNotReceive().UpdateAsync(Arg.Any<User>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenTokenAlreadyConsumed()
	{
		var ct = TestContext.Current.CancellationToken;
		const string plaintext = "oprt_consumed";
		var user = new User("user@example.com", "tester", "old-hash");
		var consumedToken = new PasswordResetToken(user.Id, HashToken(plaintext), DateTime.UtcNow.AddMinutes(15));
		consumedToken.Consume();

		// Even if the store returned it, a single-use token must not be redeemable twice.
		_resetTokenRepository.FindActiveByTokenHashAsync(HashToken(plaintext), ct).Returns(consumedToken);

		var result = await _service.ResetPasswordAsync(plaintext, "new-password-123", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Invalid or expired reset token");
		await _userRepository.DidNotReceive().UpdateAsync(Arg.Any<User>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenUserMissing()
	{
		var ct = TestContext.Current.CancellationToken;
		const string plaintext = "oprt_orphan";
		var resetToken = new PasswordResetToken(Guid.NewGuid(), HashToken(plaintext), DateTime.UtcNow.AddMinutes(15));

		_resetTokenRepository.FindActiveByTokenHashAsync(HashToken(plaintext), ct).Returns(resetToken);
		_userRepository.GetByIdAsync(resetToken.UserId, ct).Returns((User?)null);

		var result = await _service.ResetPasswordAsync(plaintext, "new-password-123", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Invalid or expired reset token");
		await _userRepository.DidNotReceive().UpdateAsync(Arg.Any<User>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenUserInactive()
	{
		var ct = TestContext.Current.CancellationToken;
		const string plaintext = "oprt_inactive";
		var user = new User("user@example.com", "tester", "old-hash");
		user.Deactivate();
		var resetToken = new PasswordResetToken(user.Id, HashToken(plaintext), DateTime.UtcNow.AddMinutes(15));

		_resetTokenRepository.FindActiveByTokenHashAsync(HashToken(plaintext), ct).Returns(resetToken);
		_userRepository.GetByIdAsync(user.Id, ct).Returns(user);

		var result = await _service.ResetPasswordAsync(plaintext, "new-password-123", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Invalid or expired reset token");
		await _userRepository.DidNotReceive().UpdateAsync(Arg.Any<User>(), ct);
	}

	[Fact]
	public async Task RequestPasswordResetAsync_DoesNotIssueToken_WhenResetUrlBaseMissing()
	{
		var ct = TestContext.Current.CancellationToken;
		var service = new AuthService(
			_userRepository,
			_tokenRepository,
			_resetTokenRepository,
			_emailSender,
			new EmailOptions { ResetUrlBase = "", ResetTokenLifetimeMinutes = 30 },
			NullLogger<AuthService>.Instance);
		var user = new User("user@example.com", "tester", "existing-hash");
		_userRepository.GetByEmailAsync("user@example.com", ct).Returns(user);

		await service.RequestPasswordResetAsync("user@example.com", ct);

		await _resetTokenRepository.DidNotReceive().InvalidateActiveForUserAsync(Arg.Any<Guid>(), ct);
		await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<PasswordResetToken>(), ct);
		await _emailSender.DidNotReceive().SendAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), ct);
	}

	[Fact]
	public async Task ResetPasswordAsync_Fails_WhenPasswordTooShort()
	{
		var ct = TestContext.Current.CancellationToken;

		var result = await _service.ResetPasswordAsync("oprt_whatever", "short", ct);

		result.Success.Should().BeFalse();
		result.ErrorMessage.Should().Be("Password must be at least 8 characters");
	}

	private static string HashToken(string token)
		=> Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
