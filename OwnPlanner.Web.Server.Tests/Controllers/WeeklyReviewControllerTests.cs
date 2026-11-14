using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Controllers;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Tests.Controllers;

public sealed class WeeklyReviewControllerTests
{
	[Theory]
	[InlineData("ok", 200)]
	[InlineData("invalid", 400)]
	[InlineData("missing", 404)]
	public async Task OccurrenceDrillDownUsesAuthenticatedInitializationAndExistingErrorSemantics(string outcome, int status)
	{
		var ct = TestContext.Current.CancellationToken;
		var service = Substitute.For<IWeeklyReviewService>();
		var initialization = Substitute.For<IPerUserAppInitializationService>();
		var id = Guid.NewGuid(); var task = Guid.NewGuid(); var date = new DateOnly(2026, 11, 13);
		if (outcome != "ok") service.OccurrencesAsync(id, task, 2, 1, date, ct)
			.Returns(Task.FromException<WeeklyReviewOccurrencePage>(outcome == "invalid" ? new ArgumentException("Invalid range.") : new KeyNotFoundException()));
		var controller = new WeeklyReviewController(service, initialization)
		{
			ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
				[new Claim(ClaimTypes.NameIdentifier, "authenticated-user")], "test")) } }
		};
		var result = await controller.Occurrences(id, task, 2, 1, date, ct);
		((ObjectResult)result).StatusCode.Should().Be(status);
		Received.InOrder(() =>
		{
			initialization.EnsureInitializedAsync(Arg.Is<SessionContext>(s => s != null && s.UserId == "authenticated-user"), ct);
			service.OccurrencesAsync(id, task, 2, 1, date, ct);
		});
	}

	[Fact]
	public async Task MissingIdentityCannotDispatchOccurrenceReadAndControllerRequiresAuthorization()
	{
		typeof(WeeklyReviewController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().ContainSingle();
		var service = Substitute.For<IWeeklyReviewService>();
		var controller = new WeeklyReviewController(service, Substitute.For<IPerUserAppInitializationService>())
		{ ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
		var read = () => controller.Occurrences(Guid.NewGuid(), ct: TestContext.Current.CancellationToken);
		await read.Should().ThrowAsync<UnauthorizedAccessException>();
		service.ReceivedCalls().Should().BeEmpty();
	}
}
