using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public class ChatControllerTests
{
	private const string TestSessionId = "test-session-id";
	private const string TestUserId = "test-user-id";

	private readonly IChatSessionManager _sessionManager = Substitute.For<IChatSessionManager>();
	private readonly ILogger<ChatController> _logger = Substitute.For<ILogger<ChatController>>();
	private readonly IPlanningService _planningService = Substitute.For<IPlanningService>();
	private readonly IOptions<ChatSettings> _chatSettings = Options.Create(new ChatSettings
	{
		Gemini = new GeminiSettings { MaxContextLengthTokens = 64 * 1024 }
	});
	private readonly ChatController _controller;

	public ChatControllerTests()
	{
		_sessionManager
			.GetOrCreateSessionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_planningService);
		_sessionManager.GetSession(Arg.Any<string>()).Returns(_planningService);
     _planningService.MaxContextLengthTokens.Returns(64 * 1024);

		_controller = CreateController();
	}

	private ChatController CreateController()
	{
		var controller = new ChatController(_sessionManager, _logger, _chatSettings);
		var claims = new[]
		{
			new Claim("SessionId", TestSessionId),
			new Claim(ClaimTypes.NameIdentifier, TestUserId),
		};
		var identity = new ClaimsIdentity(claims, "TestAuth");
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
		};
		return controller;
	}

	// --- SendMessage ---

	[Fact]
	public async Task SendMessage_EmptyMessage_ReturnsBadRequest()
	{
		var ct = TestContext.Current.CancellationToken;
		var request = new ChatRequest { Message = "" };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<BadRequestObjectResult>();
	}

	[Fact]
	public async Task SendMessage_WhitespaceMessage_ReturnsBadRequest()
	{
		var ct = TestContext.Current.CancellationToken;
		var request = new ChatRequest { Message = "   " };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<BadRequestObjectResult>();
	}

	[Fact]
	public async Task SendMessage_ValidMessage_ReturnsOkWithResponse()
	{
		var ct = TestContext.Current.CancellationToken;
     _planningService.GetResponseAsync("hello", ct).Returns(new ChatTurnResult("AI reply", 321));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<ChatResponse>().Subject;
		response.Message.Should().Be("AI reply");
		response.SessionId.Should().Be(TestSessionId);
       response.ContextLengthTokens.Should().Be(321);
      response.MaxContextLengthTokens.Should().Be(64 * 1024);
	}

	[Fact]
	public async Task SendMessage_ContextLimitReached_ReturnsBadRequest()
	{
		var ct = TestContext.Current.CancellationToken;
		_planningService.GetResponseAsync("hello", ct)
          .ThrowsAsync(new ChatContextLimitExceededException(64 * 1024, 64 * 1024));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<BadRequestObjectResult>();
	}

	[Fact]
	public async Task SendMessage_ServiceThrows_Returns500()
	{
		var ct = TestContext.Current.CancellationToken;
		_planningService.GetResponseAsync(Arg.Any<string>(), ct)
			.ThrowsAsync(new Exception("boom"));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
	}

	// --- SwitchMode ---

	[Fact]
	public async Task SwitchMode_InvalidMode_ReturnsBadRequest()
	{
		var ct = TestContext.Current.CancellationToken;
		var request = new SwitchModeRequest { Mode = "NotAMode" };

		var result = await _controller.SwitchMode(request, ct);

		result.Should().BeOfType<BadRequestObjectResult>();
	}

	[Fact]
	public async Task SwitchMode_ValidMode_ReturnsOkWithModeAndSessionId()
	{
		var ct = TestContext.Current.CancellationToken;
		var request = new SwitchModeRequest { Mode = "DayWork" };

		var result = await _controller.SwitchMode(request, ct);

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		ok.Value.Should().BeEquivalentTo(new { mode = "DayWork", sessionId = TestSessionId });
	}

	[Fact]
	public async Task SwitchMode_ServiceThrows_Returns500()
	{
		var ct = TestContext.Current.CancellationToken;
		_planningService.SwitchModeAsync(Arg.Any<PlanningMode>(), ct)
			.ThrowsAsync(new Exception("boom"));
		var request = new SwitchModeRequest { Mode = "GlobalPlanning" };

		var result = await _controller.SwitchMode(request, ct);

		result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
	}

	// --- GetModeStarterPrompts ---

	[Fact]
	public void GetModeStarterPrompts_InvalidMode_ReturnsBadRequest()
	{
		var result = _controller.GetModeStarterPrompts("NotAMode");

		result.Should().BeOfType<BadRequestObjectResult>();
	}

	[Fact]
	public void GetModeStarterPrompts_ValidMode_ReturnsPromptsForMode()
	{
		var result = _controller.GetModeStarterPrompts("GlobalPlanning");

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<ModeStarterPromptsResponse>().Subject;
		response.Mode.Should().Be("GlobalPlanning");
		response.StarterPrompts.Should().NotBeEmpty();
	}

	// --- ClearSession ---

	[Fact]
	public async Task ClearSession_Success_ReturnsOk()
	{
		var result = await _controller.ClearSession();

		await _sessionManager.Received(1).RemoveSessionAsync(TestSessionId);
		result.Should().BeOfType<OkObjectResult>();
	}

	[Fact]
	public async Task ClearSession_ServiceThrows_Returns500()
	{
		_sessionManager.RemoveSessionAsync(Arg.Any<string>())
			.ThrowsAsync(new Exception("boom"));

		var result = await _controller.ClearSession();

		result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
	}

	// --- GetSessionStatus ---

	[Fact]
	public void GetSessionStatus_ReturnsOkWithSessionInfo()
	{
		_sessionManager.GetActiveSessionCount().Returns(3);
		_planningService.CurrentMode.Returns(PlanningMode.Reflection);
		_planningService.CurrentContextLengthTokens.Returns(654);

		var result = _controller.GetSessionStatus();

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<SessionStatusResponse>().Subject;
		response.SessionId.Should().Be(TestSessionId);
		response.IsActive.Should().BeTrue();
		response.ActiveSessionsCount.Should().Be(3);
       response.CurrentMode.Should().Be("Reflection");
		response.ContextLengthTokens.Should().Be(654);
      response.MaxContextLengthTokens.Should().Be(64 * 1024);
	}

	[Fact]
	public void GetSessionStatus_WhenSessionMissing_ReturnsInactiveStatus()
	{
		_sessionManager.GetSession(TestSessionId).Returns((IPlanningService?)null);
		_sessionManager.GetActiveSessionCount().Returns(0);

		var result = _controller.GetSessionStatus();

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<SessionStatusResponse>().Subject;
		response.IsActive.Should().BeFalse();
		response.CurrentMode.Should().BeNull();
		response.ContextLengthTokens.Should().BeNull();
		response.MaxContextLengthTokens.Should().Be(64 * 1024);
	}

	// --- HealthCheck ---

	[Fact]
	public void HealthCheck_ReturnsOkWithHealthyStatus()
	{
		_sessionManager.GetActiveSessionCount().Returns(2);

		var result = _controller.HealthCheck();

		result.Should().BeOfType<OkObjectResult>();
	}
}
