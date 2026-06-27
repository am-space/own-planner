using System.Security.Claims;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Domain.Users;
using OwnPlanner.Web.Server.Authentication;

namespace OwnPlanner.Web.Server.Tests.Authentication;

public class CookiePrincipalValidatorTests
{
	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

	private static ClaimsPrincipal PrincipalFor(string? userId)
	{
		var claims = userId is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
		return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
	}

	[Fact]
	public async Task IsPrincipalValidAsync_ExistingActiveUser_ReturnsTrue()
	{
		var user = new User("user@example.com", "tester", "hash");
		_userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

		var valid = await CookiePrincipalValidator.IsPrincipalValidAsync(
			PrincipalFor(user.Id.ToString()), _userRepository, TestContext.Current.CancellationToken);

		valid.Should().BeTrue();
	}

	[Fact]
	public async Task IsPrincipalValidAsync_DeletedUser_ReturnsFalse()
	{
		var userId = Guid.NewGuid();
		_userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

		var valid = await CookiePrincipalValidator.IsPrincipalValidAsync(
			PrincipalFor(userId.ToString()), _userRepository, TestContext.Current.CancellationToken);

		valid.Should().BeFalse();
	}

	[Fact]
	public async Task IsPrincipalValidAsync_InactiveUser_ReturnsFalse()
	{
		var user = new User("user@example.com", "tester", "hash");
		user.Deactivate();
		_userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

		var valid = await CookiePrincipalValidator.IsPrincipalValidAsync(
			PrincipalFor(user.Id.ToString()), _userRepository, TestContext.Current.CancellationToken);

		valid.Should().BeFalse();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not-a-guid")]
	public async Task IsPrincipalValidAsync_MissingOrInvalidClaim_ReturnsFalse(string? userId)
	{
		var valid = await CookiePrincipalValidator.IsPrincipalValidAsync(
			PrincipalFor(userId), _userRepository, TestContext.Current.CancellationToken);

		valid.Should().BeFalse();
		await _userRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}
}
