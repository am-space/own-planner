using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public class WeeklyReportToolsTests
{
	private readonly IWeeklyReportReader _reader = Substitute.For<IWeeklyReportReader>();

	[Fact]
	public async Task GetWeeklyReport_ParsesIsoDateAndPropagatesOptionsAndCancellation()
	{
		using var source = new CancellationTokenSource();
		var report = EmptyReport();
		_reader.GetAsync(Arg.Any<WeeklyReportOptions>(), Arg.Any<CancellationToken>()).Returns(report);
		var tools = new WeeklyReportTools(_reader);

		var result = await tools.GetWeeklyReport("2026-08-17", 2, 4, source.Token);

		result.Should().BeSameAs(report);
		await _reader.Received(1).GetAsync(
			Arg.Is<WeeklyReportOptions>(options => options != null && options.StartDate == new DateOnly(2026, 8, 17) && options.TaskSampleLimit == 2 && options.OverloadedDayThreshold == 4),
			source.Token);
	}

	[Theory]
	[InlineData("2026-8-17")]
	[InlineData("2026-02-30")]
	[InlineData("2026-08-17T00:00:00Z")]
	public async Task GetWeeklyReport_InvalidDateReturnsStableErrorWithoutCallingReader(string value)
	{
		var result = await new WeeklyReportTools(_reader).GetWeeklyReport(value, cancellationToken: TestContext.Current.CancellationToken);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.GetProperty("error").GetString().Should().Be("startDate must be an ISO UTC calendar date in yyyy-MM-dd format.");
		await _reader.DidNotReceive().GetAsync(Arg.Any<WeeklyReportOptions>(), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task GetWeeklyReport_InvalidOptionReturnsStableError()
	{
		_reader.GetAsync(Arg.Any<WeeklyReportOptions>(), Arg.Any<CancellationToken>())
			.Returns<Task<WeeklyReport>>(_ => throw new ArgumentOutOfRangeException("TaskSampleLimit", "Task sample limit must be between 0 and 5."));

		var result = await new WeeklyReportTools(_reader).GetWeeklyReport(taskSampleLimit: 6, cancellationToken: TestContext.Current.CancellationToken);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.GetProperty("error").GetString().Should().Contain("between 0 and 5");
	}

	private static WeeklyReport EmptyReport() => new(
		DateTime.UnixEpoch, DateOnly.FromDateTime(DateTime.UnixEpoch), DateOnly.FromDateTime(DateTime.UnixEpoch).AddDays(7),
		"UTC", "[windowStartDate, windowEndExclusiveDate)", 5,
		new WeeklyOverallTotals(0, 0, 0, 0, 0, 0), [], [], [], new WeeklyPlanningSignals([], [], [], 0, [], 0));
}
