using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

	private readonly IAccountExportService _exportService = Substitute.For<IAccountExportService>();
	private readonly IPerUserAppInitializationService _initializationService = Substitute.For<IPerUserAppInitializationService>();
	private readonly ILogger<AccountController> _logger = Substitute.For<ILogger<AccountController>>();

	private AccountController CreateController(string? userId = TestUserId)
	{
		var controller = new AccountController(_exportService, _initializationService, _logger);
		var claims = userId is null
			? Array.Empty<Claim>()
			: new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
		var identity = new ClaimsIdentity(claims, "TestAuth");
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
		};
		return controller;
	}

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
}
