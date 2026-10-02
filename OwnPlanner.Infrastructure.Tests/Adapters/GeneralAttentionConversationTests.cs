using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Tests.Adapters;

public sealed partial class ChatSkillOrchestrationTests
{
	[Fact]
	public async Task GeneralAttentionIsImmediatelyDeclaredAndFreshBroadThenDayReadsReachProvider()
	{
		using var provider = new ScriptedProvider(
			Calls(Call("general_attention_get", new { section = "all" })), Final("Week attention."),
			Calls(Call("general_attention_get", new { section = "today" })), Final("Today only."),
			Calls(Call("general_attention_get", new { section = "all" })), Final("Fresh week attention."),
			Calls(Call("general_attention_get", new { section = "olderFocus", offset = 5, limit = 5 })), Final("More older work."));
		var mcp = new RecordingMcpAdapter();
		mcp.Results["general_attention_get"] = "{\"view\":\"all\",\"count\":2}";
		await using var adapter = CreateAdapter(provider, mcp);
		await using var planning = new PlanningService(adapter, mcp, NullLogger<PlanningService>.Instance);
		var ct = TestContext.Current.CancellationToken;
		await planning.GetResponseAsync("What needs my attention?", ct);
		Names(provider.Requests[0]).Should().Contain("general_attention_get");
		provider.Requests[1].ToString().Should().Contain("count").And.Contain("2");
		await planning.GetResponseAsync("What needs attention today only?", ct);
		mcp.Results["general_attention_get"] = "{\"view\":\"all\",\"count\":1}";
		await planning.GetResponseAsync("I completed one; what needs my attention now?", ct);
		await planning.GetResponseAsync("Show the next older focus page", ct);
		mcp.Calls.Should().Equal("general_report_get", "calendar_period_get", "general_attention_get",
			"calendar_period_get", "general_attention_get", "calendar_period_get", "general_attention_get",
			"calendar_period_get", "general_attention_get");
		mcp.Arguments.Where(a => a.Tool == "general_attention_get").Select(a => a.Values!["section"]!.ToString())
			.Should().Equal("all", "today", "all", "olderFocus");
		provider.Requests[5].ToString().Should().Contain("count");
		ContentsText(provider.Requests[0]).Should().Contain("today as the main section")
			.And.Contain("Repeat attention requests").And.Contain("never reschedule")
			.And.Contain("Missed focus plans are flexible plans").And.Contain("Do not expand a narrower day request");
		Names(provider.Requests[6]).Should().NotContain("taskitem_update");
	}

	[Theory]
	[InlineData(PlanningMode.DayWork)]
	[InlineData(PlanningMode.SystemAnalysis)]
	[InlineData(PlanningMode.Reflection)]
	public async Task AttentionDoesNotExpandSpecialistPermissions(PlanningMode mode)
	{
		using var provider = new ScriptedProvider(Calls(Call("general_attention_get", new { })), Final("Unavailable"));
		var mcp = new RecordingMcpAdapter();
		await using var adapter = CreateAdapter(provider, mcp, mode);
		await adapter.GetResponse("Attention", TestContext.Current.CancellationToken);
		mcp.Calls.Should().BeEmpty();
		provider.Requests[1].ToString().Should().Contain("not permitted for this model request");
	}
}
