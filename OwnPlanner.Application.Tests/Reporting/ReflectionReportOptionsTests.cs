using FluentAssertions;
using OwnPlanner.Application.Reporting;

namespace OwnPlanner.Application.Tests.Reporting;

public class ReflectionReportOptionsTests
{
	[Fact]
	public void Validate_DefaultsAreValid() => new ReflectionReportOptions().Invoking(options => options.Validate()).Should().NotThrow();

	[Theory]
	[InlineData(0, 3, 3)]
	[InlineData(32, 3, 3)]
	[InlineData(7, -1, 3)]
	[InlineData(7, 6, 3)]
	[InlineData(7, 3, -1)]
	[InlineData(7, 3, 6)]
	public void Validate_RejectsOutOfRangeValues(int periodDays, int taskLimit, int noteLimit) =>
		new ReflectionReportOptions(periodDays, TaskSampleLimit: taskLimit, NoteSampleLimit: noteLimit)
			.Invoking(options => options.Validate()).Should().Throw<ArgumentException>();

	[Fact]
	public void Validate_RejectsNonUtcEndInstant() =>
		new ReflectionReportOptions(EndAtUtc: new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Unspecified))
			.Invoking(options => options.Validate()).Should().Throw<ArgumentException>();
}
