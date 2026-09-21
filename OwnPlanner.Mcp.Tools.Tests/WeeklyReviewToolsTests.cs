using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.WeeklyReviews;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class WeeklyReviewToolsTests
{
	private readonly IWeeklyReviewService _service = Substitute.For<IWeeklyReviewService>();
	private WeeklyReviewTools Tools => new(_service, new WeeklyReviewActions(Substitute.For<IWeeklyReviewStore>(),
		Substitute.For<ITaskItemService>(), Substitute.For<ITaskListService>(), TimeProvider.System));

	[Fact]
	public void SdkSchemasHaveNoTenantOrPathInputs_AndExplicitOptInIsRequired()
	{
		foreach (var method in typeof(WeeklyReviewTools).GetMethods().Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length != 0))
		{
			var schema = McpServerTool.Create(method, Tools).ProtocolTool.InputSchema;
			var properties = schema.GetProperty("properties");
			properties.TryGetProperty("userId", out _).Should().BeFalse();
			properties.TryGetProperty("path", out _).Should().BeFalse();
			properties.TryGetProperty("ct", out _).Should().BeFalse();
			if (method.Name == "Configure")
			{
				schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()).Should().Equal("enabled");
				properties.GetProperty("enabled").GetProperty("type").GetString().Should().Be("boolean");
			}
		}
	}

	[Fact]
	public async Task SettingsAndTransitionsForwardOnlyExplicitValuesAndCancellation()
	{
		var ct = TestContext.Current.CancellationToken;
		var preferences = new WeeklyReviewPreferences();
		_service.GetPreferencesAsync(ct).Returns(preferences);
		(await Tools.Settings(ct)).Should().BeSameAs(preferences);
		await Tools.Configure(true, "Europe/London", 0, "17:30", ct: ct);
		await _service.Received(1).ConfigureAsync(true, "Europe/London", 0, "17:30", "telegram", ct);
		var id = Guid.NewGuid();
		await Tools.Transition(id.ToString(), "defer", "2026-09-22T18:00", ct);
		await _service.Received(1).TransitionAsync(id, "defer", "2026-09-22T18:00", ct);
	}

	[Fact]
	public async Task DisablingWithOmittedPreferencesPreservesExistingCalendar()
	{
		var ct = TestContext.Current.CancellationToken;
		_service.GetPreferencesAsync(ct).Returns(new WeeklyReviewPreferences { Enabled = true, TimeZoneId = "Europe/London", WeekStart = 0, ReminderTime = "17:30" });
		await Tools.Configure(false, ct: ct);
		await _service.Received(1).ConfigureAsync(false, "Europe/London", 0, "17:30", "telegram", ct);
	}

	[Theory]
	[InlineData("bad", "2026-09-20T00:00:00Z", null, null)]
	[InlineData("00000000-0000-0000-0000-000000000000", "bad", null, null)]
	[InlineData("00000000-0000-0000-0000-000000000000", "2026-09-20T00:00:00Z", "2026-2-30", null)]
	[InlineData("00000000-0000-0000-0000-000000000000", "2026-09-20T00:00:00Z", null, "2026-09-21T12:00:00")]
	public async Task InvalidActionInputFailsBeforeDispatch(string id, string revision, string? focus, string? due)
	{
		var action = () => Tools.Apply(id, id, revision, due is null ? "focus" : "deadline", focus, due, TestContext.Current.CancellationToken);
		await action.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task NoEligibleOfferHasNoFabricatedInvitation()
	{
		_service.OfferAsync(TestContext.Current.CancellationToken).Returns((string?)null);
		var result = JsonSerializer.SerializeToElement(await Tools.Offer(TestContext.Current.CancellationToken));
		result.GetProperty("invitation").ValueKind.Should().Be(JsonValueKind.Null);
	}
}
