using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Account;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public class AccountControllerTests
{
	private const string TestUserId = "11111111-1111-1111-1111-111111111111";
	private const string TestSessionId = "test-session-id";

	private readonly IAccountDeletionService _deletionService = Substitute.For<IAccountDeletionService>();
	private readonly IChatSessionManager _sessionManager = Substitute.For<IChatSessionManager>();
	private readonly IAuthenticationService _authenticationService = Substitute.For<IAuthenticationService>();
	private readonly ILogger<AccountController> _logger = Substitute.For<ILogger<AccountController>>();

	private AccountController CreateController(string? userId = TestUserId)
	{
		var controller = new AccountController(_deletionService, _sessionManager, _logger);

		var claims = new List<Claim> { new("SessionId", TestSessionId) };
		if (userId is not null)
		{
			claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
		}

		var identity = new ClaimsIdentity(claims, "TestAuth");
		var services = new ServiceCollection();
		services.AddSingleton(_authenticationService);

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(identity),
				RequestServices = services.BuildServiceProvider(),
			}
		};
		return controller;
	}

	[Fact]
	public async Task DeleteAccount_MissingPassword_ReturnsBadRequest()
	{
		var controller = CreateController();

		var result = await controller.DeleteAccount(new DeleteAccountRequest(""), TestContext.Current.CancellationToken);

		result.Should().BeOfType<BadRequestObjectResult>();
		await _deletionService.DidNotReceive().DeleteAccountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAccount_WrongPassword_ReturnsBadRequestAndDoesNotSignOut()
	{
		_deletionService.DeleteAccountAsync(Arg.Any<Guid>(), "wrong", Arg.Any<CancellationToken>())
			.Returns(new AccountDeletionResult(false, "Password is incorrect."));
		var controller = CreateController();

		var result = await controller.DeleteAccount(new DeleteAccountRequest("wrong"), TestContext.Current.CancellationToken);

		result.Should().BeOfType<BadRequestObjectResult>();
		await _sessionManager.DidNotReceive().RemoveSessionAsync(Arg.Any<string>());
		await _authenticationService.DidNotReceive().SignOutAsync(
			Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task DeleteAccount_Success_EvictsSessionAndSignsOut()
	{
		_deletionService.DeleteAccountAsync(Arg.Any<Guid>(), "correct", Arg.Any<CancellationToken>())
			.Returns(new AccountDeletionResult(true));
		var controller = CreateController();

		var result = await controller.DeleteAccount(new DeleteAccountRequest("correct"), TestContext.Current.CancellationToken);

		result.Should().BeOfType<OkObjectResult>();
		await _sessionManager.Received(1).RemoveSessionAsync(TestSessionId);
		await _authenticationService.Received(1).SignOutAsync(
			Arg.Any<HttpContext>(), Arg.Any<string>(), Arg.Any<AuthenticationProperties>());
	}

	[Fact]
	public async Task DeleteAccount_InvalidUserClaim_ReturnsUnauthorized()
	{
		var controller = CreateController(userId: null);

		var result = await controller.DeleteAccount(new DeleteAccountRequest("correct"), TestContext.Current.CancellationToken);

		result.Should().BeOfType<UnauthorizedObjectResult>();
		await _deletionService.DidNotReceive().DeleteAccountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}
}
