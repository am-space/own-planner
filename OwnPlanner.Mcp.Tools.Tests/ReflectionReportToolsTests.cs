using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public class ReflectionReportToolsTests
{
	private readonly IReflectionReportReader _reader = Substitute.For<IReflectionReportReader>();

	[Fact]
	public async Task GetReflectionReport_ParsesUtcInstantAndPropagatesOptionsAndCancellation()
	{
		using var source = new CancellationTokenSource();
		var report = EmptyReport();
		_reader.GetAsync(Arg.Any<ReflectionReportOptions>(), Arg.Any<CancellationToken>()).Returns(report);

		var result = await new ReflectionReportTools(_reader).GetReflectionReport(14, "2026-08-19T12:30:00.123Z", 2, 1, source.Token);

		result.Should().BeSameAs(report);
		await _reader.Received(1).GetAsync(
			Arg.Is<ReflectionReportOptions>(options => options != null && options.PeriodDays == 14 && options.EndAtUtc == new DateTime(2026, 8, 19, 12, 30, 0, 123, DateTimeKind.Utc) && options.TaskSampleLimit == 2 && options.NoteSampleLimit == 1),
			source.Token);
	}

	[Theory]
	[InlineData("2026-08-19T12:00:00")]
	[InlineData("2026-08-19T12:00:00+02:00")]
	[InlineData("08/19/2026 12:00:00Z")]
	[InlineData("not-an-instant")]
	public async Task GetReflectionReport_InvalidEndInstantReturnsStableError(string value)
	{
		var ct = TestContext.Current.CancellationToken;
		var result = await new ReflectionReportTools(_reader).GetReflectionReport(endAtUtc: value, cancellationToken: ct);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.GetProperty("error").GetString().Should().Contain("zero UTC offset");
		await _reader.DidNotReceive().GetAsync(Arg.Any<ReflectionReportOptions>(), ct);
	}

	[Fact]
	public async Task GetReflectionReport_InvalidOptionReturnsStableError()
	{
		_reader.GetAsync(Arg.Any<ReflectionReportOptions>(), Arg.Any<CancellationToken>())
			.Returns<Task<ReflectionReport>>(_ => throw new ArgumentOutOfRangeException("PeriodDays", "Period days must be between 1 and 31."));

		var result = await new ReflectionReportTools(_reader).GetReflectionReport(periodDays: 32, cancellationToken: TestContext.Current.CancellationToken);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.GetProperty("error").GetString().Should().Contain("between 1 and 31");
	}

	private static ReflectionReport EmptyReport() => new(
		DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(-7), DateTime.UnixEpoch, "UTC", "[periodStartUtc, periodEndExclusiveUtc)", [],
		new ReflectionOverallTotals(0, 0, 0, 0, 0, 0), [], [],
		new ReflectionInboxSummary(Guid.Empty, 0, []), new ReflectionSignals([], 0, [], [], 0, 0));
}
