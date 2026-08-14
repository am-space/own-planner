using FluentAssertions;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Application.Tests.Reporting;

public class StrategicReportOptionsTests
{
	[Fact]
	public void Defaults_AreCompactAndValid()
	{
		var options = new StrategicReportOptions();

		options.TaskSampleLimit.Should().Be(3);
		options.NoteSampleLimit.Should().Be(2);
		options.Invoking(value => value.Validate()).Should().NotThrow();
	}

	[Theory]
	[InlineData(-1, 2)]
	[InlineData(6, 2)]
	[InlineData(3, -1)]
	[InlineData(3, 6)]
	public void Validate_RejectsOutOfRangeLimits(int taskLimit, int noteLimit)
	{
		var options = new StrategicReportOptions(taskLimit, noteLimit);

		options.Invoking(value => value.Validate()).Should().Throw<ArgumentOutOfRangeException>();
	}
}
