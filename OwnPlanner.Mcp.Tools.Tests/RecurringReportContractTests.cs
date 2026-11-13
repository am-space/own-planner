using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class RecurringReportContractTests
{
	[Theory]
	[InlineData("general")]
	[InlineData("day")]
	[InlineData("attention")]
	public async Task SharedReportToolsPreserveCompositeIdentitiesAndAdditiveCountUnits(string kind)
	{
		var ct = TestContext.Current.CancellationToken;
		var calendar = PlanningCalendarResolver.Resolve("today", new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), "UTC");
		var series = Guid.NewGuid(); var ordinary = Guid.NewGuid();
		var samples = new ReportWorkSample[] {
			new(series, new(2026, 11, 13), new(2026, 11, 13), null, PlanningWorkStatus.Pending, null, "Series", false),
			new(series, new(2026, 11, 15), new(2026, 11, 15), null, PlanningWorkStatus.Pending, null, "Series", false),
			new(ordinary, null, new(2026, 11, 15), null, PlanningWorkStatus.Pending, null, "Ordinary", false) };
		var section = new ReportWorkSection(new(1, 1, 2), 3, 2, 0, 0, 0, 1, 5, false, samples);
		var work = new ReportWork(calendar, new(calendar.StartDate, calendar.EndExclusiveDate), calendar.StartsAtUtc, calendar.EndsAtUtc,
			section, section, new(new(0, 0, 0), 0, 0, 0, 0, 0, 0, 5, false, []), [], [], [], []);
		var reader = Substitute.For<IGeneralReportReader>();
		reader.GetAsync(ct).Returns(GeneralReportBuilder.Build(calendar.AsOfUtc, [], [], 0, work));
		reader.GetDayAsync(Arg.Any<DayReportOptions>(), ct).Returns(DayReportBuilder.Build(calendar, [], work: work));
		var week = PlanningCalendarResolver.Resolve("thisWeek", calendar.AsOfUtc, "UTC");
		reader.GetAttentionAsync(Arg.Any<GeneralAttentionOptions>(), ct).Returns(GeneralAttentionBuilder.Build(week, [], work: work));
		var tools = new GeneralReportTools(reader);
		var result = kind switch { "general" => await tools.GetGeneralReport(ct), "day" => await tools.GetDayReport(cancellationToken: ct), _ => await tools.GetAttention(cancellationToken: ct) };
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		var pending = json.GetProperty("work").GetProperty("pending");
		pending.GetProperty("counts").GetProperty("taskCount").GetInt32().Should().Be(2);
		pending.GetProperty("counts").GetProperty("workItemCount").GetInt32().Should().Be(3);
		var rows = pending.GetProperty("samples").EnumerateArray().ToArray();
		rows.Count(r => r.GetProperty("taskId").GetGuid() == series).Should().Be(2);
		rows.Take(2).Select(r => r.GetProperty("occurrenceDate").GetString()).Should().Equal("2026-11-13", "2026-11-15");
		rows[2].GetProperty("occurrenceDate").ValueKind.Should().Be(JsonValueKind.Null);
		rows.SelectMany(r => r.EnumerateObject()).Should().NotContain(p => p.Name == "dueAt");
	}

	[Fact]
	public void EveryReportToolExplainsSelectionIdentityCountsAndAdministrativeClosure()
	{
		foreach (var type in new[] { typeof(GeneralReportTools), typeof(WeeklyReportTools), typeof(ReflectionReportTools), typeof(StrategicReportTools) })
		foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<DescriptionAttribute>() is not null))
		{
			var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
			description.Should().Contain("TaskId plus OccurrenceDate").And.Contain("CompletionActivity")
				.And.Contain("Parent closure adds no completed work").And.Contain("not actionable occurrences");
		}
	}
}
