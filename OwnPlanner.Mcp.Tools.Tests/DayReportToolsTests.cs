using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class DayReportToolsTests
{
	[Fact]
	public async Task QueryDelegatesOptionsAndCancellationAndSerializesBoundedReasons()
	{
		var ct = TestContext.Current.CancellationToken;
		var reader = Substitute.For<IGeneralReportReader>();
		var period = PlanningCalendarResolver.Resolve("today", DateTime.UnixEpoch, null);
		var report = DayReportBuilder.Build(period, [], new("overdue", 2, 10));
		reader.GetDayAsync(new("overdue", 2, 10), ct).Returns(report);
		var result = await new GeneralReportTools(reader).GetDayReport("overdue", 2, 10, ct);
		result.Should().BeSameAs(report);
		await reader.Received(1).GetDayAsync(new("overdue", 2, 10), ct);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		json.GetProperty("view").GetString().Should().Be("overdue");
		json.GetProperty("period").GetProperty("fallbackExplanation").GetString().Should().Contain("UTC");
		json.GetProperty("sections")[0].GetProperty("offset").GetInt32().Should().Be(2);
		json.GetProperty("sections")[0].GetProperty("limit").GetInt32().Should().Be(10);
		json.GetProperty("tasks").GetArrayLength().Should().Be(0);
	}

	[Theory]
	[InlineData("invalid", 0, 5)]
	[InlineData("today", -1, 5)]
	[InlineData("today", 0, 0)]
	[InlineData("today", 0, 21)]
	[InlineData("all", 1, 5)]
	public async Task InvalidOptionsReturnErrorsBeforeReading(string section, int offset, int limit)
	{
		var reader = Substitute.For<IGeneralReportReader>();
		var result = await new GeneralReportTools(reader).GetDayReport(section, offset, limit, TestContext.Current.CancellationToken);
		JsonSerializer.SerializeToElement(result).GetProperty("error").GetString().Should().NotBeNullOrEmpty();
		reader.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task CancellationAndUnexpectedReaderFailuresPropagateWithoutPartialResults()
	{
		var ct = TestContext.Current.CancellationToken;
		var reader = Substitute.For<IGeneralReportReader>();
		var tools = new GeneralReportTools(reader);
		reader.GetDayAsync(Arg.Any<DayReportOptions>(), ct).ThrowsAsync(new OperationCanceledException(ct));
		var canceled = () => tools.GetDayReport(cancellationToken: ct);
		await canceled.Should().ThrowAsync<OperationCanceledException>();
		reader.GetDayAsync(Arg.Any<DayReportOptions>(), ct).ThrowsAsync(new InvalidOperationException("Unavailable"));
		await canceled.Should().ThrowAsync<InvalidOperationException>();
	}

	[Fact]
	public void SharedStdioSchemaIsReadOnlyOptionalAndCannotSelectTenantOrCalendar()
	{
		var tool = McpServerTool.Create(typeof(GeneralReportTools).GetMethod(nameof(GeneralReportTools.GetDayReport))!,
			new GeneralReportTools(Substitute.For<IGeneralReportReader>())).ProtocolTool;
		tool.Name.Should().Be("day_report_get");
		tool.Annotations!.ReadOnlyHint.Should().BeTrue();
		tool.Annotations.IdempotentHint.Should().BeTrue();
		tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("section", "offset", "limit");
		if (tool.InputSchema.TryGetProperty("required", out var required)) required.EnumerateArray().Should().BeEmpty();
	}
}
