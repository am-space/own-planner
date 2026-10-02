using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Tests.Chat;

public partial class PlanningServiceTests
{
	[Theory]
	[InlineData(PlanningMode.General, "general_report_get", "thisWeek")]
	[InlineData(PlanningMode.WeekPlanning, "weekly_report_get", "thisWeek")]
	[InlineData(PlanningMode.DayWork, "day_report_get", "today")]
	[InlineData(PlanningMode.Reflection, "reflection_report_get", "lastWeek")]
	public async Task CalendarModesPreloadExplicitPeriodsAndReceiveFreshContextAcrossMidnight(PlanningMode mode, string tool, string period)
	{
		var ct = TestContext.Current.CancellationToken;
		var contexts = new List<string>();
		_chatAdapter.When(a => a.SetRequestContext(Arg.Any<string>())).Do(c => contexts.Add(c.Arg<string>()!));
		_mcpAdapter.CallToolAsync("calendar_period_get", Arg.Any<IReadOnlyDictionary<string, object?>?>(), ct)
			.Returns("{\"today\":\"2026-10-02\",\"timeZone\":\"Asia/Tokyo\"}", "{\"today\":\"2026-10-03\",\"timeZone\":\"Asia/Tokyo\"}");
		await _svc.SwitchModeAsync(mode, ct);
		await _svc.GetResponseAsync("What is planned today?", ct);
		await _svc.GetResponseAsync("What is planned today?", ct);
		await _mcpAdapter.Received(1).CallToolAsync(tool,
			Arg.Is<IReadOnlyDictionary<string, object?>?>(a => mode == PlanningMode.DayWork ? a == null : a != null && (string?)a["calendarPeriod"] == period), ct);
		contexts.Should().HaveCount(2);
		contexts[0].Should().Contain("2026-10-02").And.Contain("Asia/Tokyo");
		contexts[1].Should().Contain("2026-10-03").And.NotContain("2026-10-02");
		_chatAdapter.Received(1).ResetChatSession(Arg.Is<string>(p => p != null && p.Contains("For every new date-scoped report request, fetch fresh data") &&
			p.Contains("last seven days is the rolling 168 hours") && p.Contains("If fallbackExplanation is present")), Arg.Any<IReadOnlyList<string>?>());
	}

	[Fact]
	public async Task CalendarLookupCancellationStopsTheTurn()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.DayWork, ct);
		_mcpAdapter.CallToolAsync("calendar_period_get", Arg.Any<IReadOnlyDictionary<string, object?>?>(), ct)
			.ThrowsAsync(new OperationCanceledException(ct));
		var action = () => _svc.GetResponseAsync("Today?", ct);
		await action.Should().ThrowAsync<OperationCanceledException>();
		await _chatAdapter.DidNotReceive().GetResponse(Arg.Any<string>(), ct);
	}

	[Fact]
	public async Task FailedCalendarLookupMarksOldSnapshotAsStale()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.General, ct);
		_mcpAdapter.CallToolAsync("calendar_period_get", Arg.Any<IReadOnlyDictionary<string, object?>?>(), ct).ThrowsAsync(new InvalidOperationException());
		await _svc.GetResponseAsync("Today?", ct);
		_chatAdapter.Received(1).SetRequestContext(Arg.Is<string>(s => s != null && s.Contains("lookup failed") && s.Contains("do not treat the initial snapshot as live")));
	}

	[Fact]
	public async Task StrategicModeClearsCalendarContextAndDoesNotFetchCalendar()
	{
		var ct = TestContext.Current.CancellationToken;
		await _svc.SwitchModeAsync(PlanningMode.GlobalPlanning, ct);
		await _svc.GetResponseAsync("Review alignment", ct);
		_chatAdapter.Received(1).SetRequestContext(string.Empty);
		await _mcpAdapter.DidNotReceive().CallToolAsync("calendar_period_get", Arg.Any<IReadOnlyDictionary<string, object?>?>(), ct);
	}
}
