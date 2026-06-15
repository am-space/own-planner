using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Usage;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public class ChatControllerTests
{
	private const string TestSessionId = "test-session-id";
	private const string TestUserId = "test-user-id";

	private static readonly DateTimeOffset TestResetAt = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

	private readonly IChatSessionManager _sessionManager = Substitute.For<IChatSessionManager>();
	private readonly IUsageQuotaService _usageQuotaService = Substitute.For<IUsageQuotaService>();
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
		_usageQuotaService.CheckAndReserveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new UsageStatus(200, 1, 199, TestResetAt));
		_usageQuotaService.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new UsageStatus(200, 5, 195, TestResetAt));

		_controller = CreateController();
	}

	private ChatController CreateController()
	{
		var controller = new ChatController(_sessionManager, _usageQuotaService, _logger, _chatSettings);
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
     _planningService.GetResponseAsync("hello", ct).Returns(new ChatTurnResult("AI reply", 321, 1500, 450));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<ChatResponse>().Subject;
		response.Message.Should().Be("AI reply");
		response.SessionId.Should().Be(TestSessionId);
       response.ContextLengthTokens.Should().Be(321);
      response.MaxContextLengthTokens.Should().Be(64 * 1024);
		response.RemainingDailyQuota.Should().Be(199);
		response.QuotaResetAtUtc.Should().Be(TestResetAt);
		await _usageQuotaService.Received(1).RecordTokensAsync(TestUserId, 1500, 450, ct);
	}

	[Fact]
	public async Task SendMessage_DailyLimitReached_Returns429WithRetryAfter()
	{
		var ct = TestContext.Current.CancellationToken;
		_usageQuotaService.CheckAndReserveAsync(TestUserId, ct)
			.ThrowsAsync(new UsageQuotaExceededException(UsageLimitKind.Daily, 3600, 0, TestResetAt));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
		_controller.Response.Headers.RetryAfter.ToString().Should().Be("3600");
		// A rejected request must never reach the model.
		await _planningService.DidNotReceive().GetResponseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SendMessage_BurstLimitReached_Returns429()
	{
		var ct = TestContext.Current.CancellationToken;
		_usageQuotaService.CheckAndReserveAsync(TestUserId, ct)
			.ThrowsAsync(new UsageQuotaExceededException(UsageLimitKind.Burst, 12, null, TestResetAt));
		var request = new ChatRequest { Message = "hello" };

		var result = await _controller.SendMessage(request, ct);

		result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
		_controller.Response.Headers.RetryAfter.ToString().Should().Be("12");
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
	public async Task GetSessionStatus_ReturnsOkWithSessionInfo()
	{
		var ct = TestContext.Current.CancellationToken;
		_sessionManager.GetActiveSessionCount().Returns(3);
		_planningService.CurrentMode.Returns(PlanningMode.Reflection);
		_planningService.CurrentContextLengthTokens.Returns(654);

		var result = await _controller.GetSessionStatus(ct);

		var ok = result.Should().BeOfType<OkObjectResult>().Subject;
		var response = ok.Value.Should().BeOfType<SessionStatusResponse>().Subject;
		response.SessionId.Should().Be(TestSessionId);
		response.IsActive.Should().BeTrue();
		response.ActiveSessionsCount.Should().Be(3);
       response.CurrentMode.Should().Be("Reflection");
		response.ContextLengthTokens.Should().Be(654);
      response.MaxContextLengthTokens.Should().Be(64 * 1024);
		response.DailyQuotaLimit.Should().Be(200);
		response.DailyQuotaUsed.Should().Be(5);
		response.RemainingDailyQuota.Should().Be(195);
		response.QuotaResetAtUtc.Should().Be(TestResetAt);
	}

	[Fact]
	public async Task GetSessionStatus_WhenSessionMissing_ReturnsInactiveStatus()
	{
		var ct = TestContext.Current.CancellationToken;
		_sessionManager.GetSession(TestSessionId).Returns((IPlanningService?)null);
		_sessionManager.GetActiveSessionCount().Returns(0);

		var result = await _controller.GetSessionStatus(ct);

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
