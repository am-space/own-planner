using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class GeneralReportToolsTests
{
	[Fact]
	public async Task GetGeneralReport_DelegatesReadAndCancellation_WithStableJsonContract()
	{
		var ct = TestContext.Current.CancellationToken;
		var reader = Substitute.For<IGeneralReportReader>();
		var report = GeneralReportBuilder.Build(DateTime.UnixEpoch, [], [], 0);
		reader.GetAsync(ct).Returns(report);
		var result = await new GeneralReportTools(reader).GetGeneralReport(ct);
		result.Should().BeSameAs(report);
		await reader.Received(1).GetAsync(ct);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		json.GetProperty("timeZone").GetString().Should().Be("UTC");
		json.GetProperty("today").GetProperty("remaining").GetProperty("sampleLimit").GetInt32().Should().Be(5);
		json.GetProperty("commitments").GetProperty("nearest").GetProperty("sampleLimit").GetInt32().Should().Be(3);
		json.GetProperty("direction").GetProperty("sampleLimit").GetInt32().Should().Be(3);
	}

	[Fact]
	public async Task GetGeneralReport_PropagatesReaderFailureWithoutReturningPartialSnapshot()
	{
		var reader = Substitute.For<IGeneralReportReader>();
		reader.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Unavailable"));
		var action = () => new GeneralReportTools(reader).GetGeneralReport(TestContext.Current.CancellationToken);
		await action.Should().ThrowAsync<InvalidOperationException>();
	}
}
