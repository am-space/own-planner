using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public class StrategicReportToolsTests
{
	private readonly IStrategicReportReader _reader = Substitute.For<IStrategicReportReader>();

	[Fact]
	public async Task GetStrategicReport_UsesDefaultLimitsAndReturnsReport()
	{
		var ct = TestContext.Current.CancellationToken;
		var report = EmptyReport();
		_reader.GetAsync(Arg.Any<StrategicReportOptions>(), Arg.Any<CancellationToken>()).Returns(report);
		var tools = new StrategicReportTools(_reader);

		var result = await tools.GetStrategicReport(cancellationToken: ct);

		result.Should().BeSameAs(report);
		await _reader.Received(1).GetAsync(
			Arg.Is<StrategicReportOptions>(options => options != null && options.TaskSampleLimit == 3 && options.NoteSampleLimit == 2),
			ct);
	}

	[Fact]
	public async Task GetStrategicReport_PreservesSignalNamesAndBoundedCounts()
	{
		var report = EmptyReport() with
		{
			Signals = new StrategicStructuralSignals([new(Guid.NewGuid(), "Delivered")],
				[new(Guid.NewGuid(), "Reference")], [], 7, [new(Guid.NewGuid(), "New")])
		};
		_reader.GetAsync(Arg.Any<StrategicReportOptions>(), Arg.Any<CancellationToken>()).Returns(report);
		var result = await new StrategicReportTools(_reader).GetStrategicReport(0, 0, TestContext.Current.CancellationToken);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		var signals = json.GetProperty("signals");
		signals.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
			["activeGoalsWithoutActiveTasks", "contextsWithoutActiveTasks", "tasksWithoutGoal", "tasksWithoutGoalCount", "contextsWithoutTaskOrNoteLists"]);
		signals.GetProperty("activeGoalsWithoutActiveTasks")[0].GetProperty("name").GetString().Should().Be("Delivered");
		signals.GetProperty("tasksWithoutGoal").GetArrayLength().Should().Be(0);
		signals.GetProperty("tasksWithoutGoalCount").GetInt32().Should().Be(7);
		await _reader.Received(1).GetAsync(Arg.Is<StrategicReportOptions>(options =>
			options != null && options.TaskSampleLimit == 0 && options.NoteSampleLimit == 0), TestContext.Current.CancellationToken);
	}

	[Fact]
	public void ToolDescriptionExplainsSignalScopeAndEvidenceLimits()
	{
		var description = typeof(StrategicReportTools).GetMethod(nameof(StrategicReportTools.GetStrategicReport))!
			.GetCustomAttribute<DescriptionAttribute>()!.Description;
		description.Should().Contain("incomplete, non-trashed tasks in non-archived lists")
			.And.Contain("does not mean no linked tasks ever or historical inactivity")
			.And.Contain("not links to existing inactive goals")
			.And.Contain("not established defects or urgency")
			.And.Contain("Samples are bounded, previews may be truncated")
			.And.Contain("fresh evidence when current state matters");
	}

	[Fact]
	public async Task GetStrategicReport_PropagatesCancellation()
	{
		using var source = new CancellationTokenSource();
		var tools = new StrategicReportTools(_reader);

		await tools.GetStrategicReport(cancellationToken: source.Token);

		await _reader.Received(1).GetAsync(Arg.Any<StrategicReportOptions>(), source.Token);
	}

	[Fact]
	public async Task GetStrategicReport_InvalidLimitReturnsStableError()
	{
		var ct = TestContext.Current.CancellationToken;
		_reader.GetAsync(Arg.Any<StrategicReportOptions>(), Arg.Any<CancellationToken>())
			.Returns<Task<StrategicReport>>(_ => throw new ArgumentOutOfRangeException("TaskSampleLimit", "Task sample limit must be between 0 and 5."));
		var tools = new StrategicReportTools(_reader);

		var result = await tools.GetStrategicReport(taskSampleLimit: 6, cancellationToken: ct);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

		json.GetProperty("error").GetString().Should().Contain("between 0 and 5");
	}

	private static StrategicReport EmptyReport() => new(
		DateTime.UnixEpoch,
		new StrategicOverallTotals(0, 0, 0, 0, 0, 0, 0, 0),
		[], [],
		new StrategicStructuralSignals([], [], [], 0, []));
}
