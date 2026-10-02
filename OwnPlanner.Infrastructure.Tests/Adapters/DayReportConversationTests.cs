using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Fact]
	public async Task DayReportIsImmediatelyDeclaredAndRefreshedAfterCompletionAndRepeatedRequests()
	{
		using var provider = new ScriptedProvider(Calls(Call("day_report_get", new { })), Final("Today"),
			Calls(Call("taskitem_complete", new { id = "task" })), Calls(Call("day_report_get", new { })), Final("Remaining"),
			Calls(Call("day_report_get", new { })), Final("Fresh remaining"),
			Calls(Call("day_report_get", new { section = "overdue", offset = 5, limit = 5 })), Final("More overdue"));
		var mcp = new RecordingMcpAdapter(); mcp.Results["day_report_get"] = "{\"totalCount\":2}";
		await using var adapter = CreateAdapter(provider, mcp, PlanningMode.DayWork);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.SwitchModeAsync(PlanningMode.DayWork, ct);
		await planning.GetResponseAsync("What should I tackle first? I have 20 minutes.", ct);
		Names(provider.Requests[0]).Should().Contain("day_report_get").And.NotContain("general_attention_get", "taskitem_update", "skill_load");
		var prompt = ContentsText(provider.Requests[0]);
		prompt.Should().Contain("IsImportant").And.Contain("remaining work").And.Contain("not automatically first")
			.And.Contain("only when necessary").And.Contain("Do not invent a schedule")
			.And.Contain("No automatic rescheduling");
		mcp.Results["day_report_get"] = "{\"totalCount\":1}";
		await planning.GetResponseAsync("Complete the first task and show what is left.", ct);
		await planning.GetResponseAsync("What is left now?", ct);
		await planning.GetResponseAsync("More overdue examples please", ct);
		mcp.Calls.Should().Equal("day_report_get", "calendar_period_get", "day_report_get", "calendar_period_get", "taskitem_complete",
			"day_report_get", "calendar_period_get", "day_report_get", "calendar_period_get", "day_report_get");
		provider.Requests[4].ToString().Should().Contain("totalCount");
		mcp.Calls.Count(t => t == "taskitem_complete").Should().Be(1);
		mcp.Arguments.Last().Values!["section"]!.ToString().Should().Be("overdue");
	}

	[Theory]
	[InlineData(PlanningMode.General)]
	[InlineData(PlanningMode.WeekPlanning)]
	[InlineData(PlanningMode.Reflection)]
	[InlineData(PlanningMode.SystemAnalysis)]
	public async Task DailyReadDoesNotBroadenOtherModePermissions(PlanningMode mode)
	{
		using var provider = new ScriptedProvider(Calls(Call("day_report_get", new { })), Final("Unavailable"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await adapter.GetResponse("Daily query", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
	}
}
