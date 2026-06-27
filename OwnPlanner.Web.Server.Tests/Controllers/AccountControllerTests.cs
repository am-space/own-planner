using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Account;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public class AccountControllerTests
{
	private const string TestUserId = "11111111-1111-1111-1111-111111111111";
	private const string TestSessionId = "test-session-id";

	private readonly IAccountExportService _exportService = Substitute.For<IAccountExportService>();
	private readonly IAccountDeletionService _deletionService = Substitute.For<IAccountDeletionService>();
	private readonly IPerUserAppInitializationService _initializationService = Substitute.For<IPerUserAppInitializationService>();
	private readonly IChatSessionManager _sessionManager = Substitute.For<IChatSessionManager>();
	private readonly IAuthenticationService _authenticationService = Substitute.For<IAuthenticationService>();
	private readonly ILogger<AccountController> _logger = Substitute.For<ILogger<AccountController>>();

	private AccountController CreateController(string? userId = TestUserId)
	{
		var controller = new AccountController(
			_exportService, _deletionService, _initializationService, _sessionManager, _logger);

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

	// --- ExportData ---

	[Fact]
	public async Task ExportData_AuthenticatedUser_ReturnsZipFileResult()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"ownplanner-export-test-{Guid.NewGuid():N}.zip");
		await File.WriteAllBytesAsync(tempFile, [1, 2, 3], TestContext.Current.CancellationToken);
		_exportService.CreateExportAsync(Arg.Any<CancellationToken>())
			.Returns(new AccountExport(tempFile, "ownplanner-export-20260627.zip", "application/zip"));

		var controller = CreateController();

		try
		{
			var result = await controller.ExportData(TestContext.Current.CancellationToken);

			var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
			fileResult.ContentType.Should().Be("application/zip");
			fileResult.FileDownloadName.Should().Be("ownplanner-export-20260627.zip");

			// The per-user database must be initialized (migrated/seeded) before it is snapshotted,
			// so a user who exports before ever chatting doesn't get an empty file.
			Received.InOrder(() =>
			{
				_initializationService.EnsureInitializedAsync(
					Arg.Is<SessionContext>(c => c.UserId == TestUserId), Arg.Any<CancellationToken>());
				_exportService.CreateExportAsync(Arg.Any<CancellationToken>());
			});

			// Dispose the result stream so the FileStream releases its handle before cleanup.
			await fileResult.FileStream.DisposeAsync();
		}
		finally
		{
			if (File.Exists(tempFile))
			{
				File.Delete(tempFile);
			}
		}
	}

	[Fact]
	public async Task ExportData_InvalidUserClaim_ReturnsUnauthorized()
	{
		var controller = CreateController(userId: null);

		var result = await controller.ExportData(TestContext.Current.CancellationToken);

		result.Should().BeOfType<UnauthorizedObjectResult>();
		await _initializationService.DidNotReceive().EnsureInitializedAsync(Arg.Any<SessionContext>(), Arg.Any<CancellationToken>());
		await _exportService.DidNotReceive().CreateExportAsync(Arg.Any<CancellationToken>());
	}

	// --- DeleteAccount ---

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
