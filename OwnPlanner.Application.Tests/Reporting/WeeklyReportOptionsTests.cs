using FluentAssertions;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Application.Tests.Reporting;

public class WeeklyReportOptionsTests
{
	[Fact]
	public void Validate_DefaultsAreValid() => new WeeklyReportOptions().Invoking(options => options.Validate()).Should().NotThrow();

	[Theory]
	[InlineData(-1, 5)]
	[InlineData(6, 5)]
	[InlineData(3, 0)]
	[InlineData(3, 21)]
	public void Validate_OutOfRangeValuesThrow(int sampleLimit, int threshold) =>
		new WeeklyReportOptions(TaskSampleLimit: sampleLimit, OverloadedDayThreshold: threshold)
			.Invoking(options => options.Validate()).Should().Throw<ArgumentOutOfRangeException>();
}
