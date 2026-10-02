using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Theory]
	[InlineData(PlanningMode.General, "general_report_get", "thisWeek")]
	[InlineData(PlanningMode.WeekPlanning, "weekly_report_get", "nextWeek")]
	[InlineData(PlanningMode.DayWork, "day_report_get", "today")]
	[InlineData(PlanningMode.Reflection, "reflection_report_get", "lastWeek")]
	public async Task NamedReportRefreshesAndCurrentCalendarReachActualProviderWithoutReplayingStaleContext(PlanningMode mode, string tool, string period)
	{
		using var provider = new ScriptedProvider(Calls(Call(tool, mode == PlanningMode.DayWork ? new { } : (object)new { calendarPeriod = period })), Final("First report."),
			Calls(Call(tool, mode == PlanningMode.DayWork ? new { } : (object)new { calendarPeriod = period })), Final("Fresh report."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["calendar_period_get"] = "{\"today\":\"2026-10-02\",\"timeZone\":\"Asia/Tokyo\"}";
		mcp.Results[tool] = "{\"asOfUtc\":\"2026-10-02T14:59:00Z\",\"taskCount\":1}";
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.SwitchModeAsync(mode, ct);
		await planning.GetResponseAsync("Report the requested period", ct);
		mcp.Results["calendar_period_get"] = "{\"today\":\"2026-10-03\",\"timeZone\":\"Asia/Tokyo\"}";
		mcp.Results[tool] = "{\"asOfUtc\":\"2026-10-02T15:01:00Z\",\"taskCount\":2}";
		await planning.GetResponseAsync("Report the requested period again", ct);
		mcp.Calls.Should().Equal(tool, "calendar_period_get", tool, "calendar_period_get", tool);
		var first = provider.Requests[0].GetProperty("systemInstruction").ToString();
		var second = provider.Requests[2].GetProperty("systemInstruction").ToString();
		first.Should().Contain("2026-10-02").And.NotContain("2026-10-03");
		second.Should().Contain("2026-10-03").And.NotContain("2026-10-02");
		provider.Requests[^1].GetProperty("systemInstruction").ToString().Should().Contain("2026-10-03");
		provider.Requests[^1].GetProperty("contents").ToString().Should().Contain("taskCount").And.Contain("15:01");
		ContentsText(provider.Requests[0]).Should().Contain("FocusAt stores a calendar date").And.Contain("last-day targeting rule");
		mcp.Arguments.Where(a => a.Tool == tool).Skip(1).Should().OnlyContain(a => mode == PlanningMode.DayWork ? a.Values!.Count == 0 : a.Values != null && a.Values["calendarPeriod"]!.ToString() == period);
	}
}
