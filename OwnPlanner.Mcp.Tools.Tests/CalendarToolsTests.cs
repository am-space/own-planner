using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Common;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Mcp.Tools.Tests;

public sealed class CalendarToolsTests
{
	[Fact]
	public async Task LocalTodayFocusLookupResolvesFreshDateEveryCallAndKeepsPagination()
	{
		var ct = TestContext.Current.CancellationToken;
		var calendar = Substitute.For<IPlanningCalendar>();
		var tasks = Substitute.For<ITaskItemService>();
		var before = PlanningCalendarResolver.Resolve("today", Utc("2026-10-02T14:59:00Z"), "Asia/Tokyo");
		var after = PlanningCalendarResolver.Resolve("today", Utc("2026-10-02T15:01:00Z"), "Asia/Tokyo");
		calendar.ResolveAsync("today", ct).Returns(before, after);
		tasks.ListByFocusDatePagedAsync(Arg.Any<DateTime>(), false, 25, 25, ct).Returns(new PagedResult<TaskItemDto>([], 50, 25, 25));
		var tools = new TaskItemTools(tasks, calendar);
		var first = JsonSerializer.SerializeToElement(await tools.ListTasksByFocusDate(limit: 25, offset: 25, calendarPeriod: "today", cancellationToken: ct), WebJson);
		var second = JsonSerializer.SerializeToElement(await tools.ListTasksByFocusDate(limit: 25, offset: 25, calendarPeriod: "today", cancellationToken: ct), WebJson);
		first.GetProperty("calendar").GetProperty("today").GetString().Should().Be("2026-10-02");
		second.GetProperty("calendar").GetProperty("today").GetString().Should().Be("2026-10-03");
		second.GetProperty("totalCount").GetInt32().Should().Be(50);
		second.GetProperty("offset").GetInt32().Should().Be(25);
		second.GetProperty("hasMore").GetBoolean().Should().BeTrue();
		await tasks.Received(1).ListByFocusDatePagedAsync(Utc("2026-10-02T00:00:00Z"), false, 25, 25, ct);
		await tasks.Received(1).ListByFocusDatePagedAsync(Utc("2026-10-03T00:00:00Z"), false, 25, 25, ct);
	}

	[Theory]
	[InlineData("thisWeek", null)]
	[InlineData("today", "2026-10-02")]
	public async Task ConflictingDayOptionsFailBeforeReads(string period, string? focusDate)
	{
		var tasks = Substitute.For<ITaskItemService>(); var calendar = Substitute.For<IPlanningCalendar>();
		var result = await new TaskItemTools(tasks, calendar).ListTasksByFocusDate(focusDate, calendarPeriod: period, cancellationToken: TestContext.Current.CancellationToken);
		JsonSerializer.SerializeToElement(result).GetProperty("error").GetString().Should().Contain("without focusDate");
		tasks.ReceivedCalls().Should().BeEmpty(); calendar.ReceivedCalls().Should().BeEmpty();
	}

	[Fact]
	public async Task SharedCalendarGetterPassesPeriodCancellationAndValidationErrors()
	{
		var ct = TestContext.Current.CancellationToken;
		var calendar = Substitute.For<IPlanningCalendar>();
		var expected = PlanningCalendarResolver.Resolve("lastWeek", DateTime.UnixEpoch, "UTC");
		calendar.ResolveAsync("lastWeek", ct).Returns(expected);
		var tools = new PlanningCalendarTools(calendar);
		(await tools.GetPeriod("lastWeek", ct)).Should().BeSameAs(expected);
		calendar.ResolveAsync("wrong", ct).Returns<Task<PlanningPeriod>>(_ => throw new ArgumentException("Unknown calendar period"));
		JsonSerializer.SerializeToElement(await tools.GetPeriod("wrong", ct)).GetProperty("error").GetString().Should().Be("Unknown calendar period");
		calendar.ResolveAsync("today", ct).Returns<Task<PlanningPeriod>>(_ => throw new OperationCanceledException(ct));
		var action = () => tools.GetPeriod(cancellationToken: ct);
		await action.Should().ThrowAsync<OperationCanceledException>();
	}

	[Theory]
	[InlineData("thisWeek")]
	[InlineData("nextWeek")]
	[InlineData("nextSevenDays")]
	public async Task WeeklyToolPassesNamedWindowWithoutChangingLegacyDefaults(string period)
	{
		var ct = TestContext.Current.CancellationToken;
		var reader = Substitute.For<IWeeklyReportReader>();
		await new WeeklyReportTools(reader).GetWeeklyReport(cancellationToken: ct, calendarPeriod: period);
		await reader.Received(1).GetAsync(Arg.Is<WeeklyReportOptions>(o => o != null && o.CalendarPeriod == period && o.StartDate == null && o.TaskSampleLimit == 3), ct);
	}

	[Fact]
	public async Task ReflectionAndGeneralToolsPassExplicitLocalPeriods()
	{
		var ct = TestContext.Current.CancellationToken;
		var reflection = Substitute.For<IReflectionReportReader>();
		await new ReflectionReportTools(reflection).GetReflectionReport(cancellationToken: ct, calendarPeriod: "lastWeek");
		await reflection.Received(1).GetAsync(Arg.Is<ReflectionReportOptions>(o => o != null && o.CalendarPeriod == "lastWeek" && o.EndAtUtc == null && o.PeriodDays == 7), ct);
		var general = Substitute.For<IGeneralReportReader>();
		await new GeneralReportTools(general).GetGeneralReport(ct, "thisWeek");
		await general.Received(1).GetCalendarAsync("thisWeek", ct);
		await general.DidNotReceive().GetAsync(ct);
	}

	[Fact]
	public void StdioSchemasAddOnlyOptionalCalendarArgumentsAndExposeReadOnlyGetter()
	{
		var weekly = new WeeklyReportTools(Substitute.For<IWeeklyReportReader>());
		var reflection = new ReflectionReportTools(Substitute.For<IReflectionReportReader>());
		var general = new GeneralReportTools(Substitute.For<IGeneralReportReader>());
		var tasks = new TaskItemTools(Substitute.For<ITaskItemService>(), Substitute.For<IPlanningCalendar>());
		foreach (var (type, method, instance, oldFields) in new (Type, string, object, string[])[]
		{
			(typeof(WeeklyReportTools), nameof(WeeklyReportTools.GetWeeklyReport), weekly, ["startDate", "taskSampleLimit", "overloadedDayThreshold"]),
			(typeof(ReflectionReportTools), nameof(ReflectionReportTools.GetReflectionReport), reflection, ["periodDays", "endAtUtc", "taskSampleLimit", "noteSampleLimit"]),
			(typeof(GeneralReportTools), nameof(GeneralReportTools.GetGeneralReport), general, []),
			(typeof(TaskItemTools), nameof(TaskItemTools.ListTasksByFocusDate), tasks, ["focusDate", "includeCompleted", "limit", "offset"])
		})
		{
			var tool = McpServerTool.Create(type.GetMethod(method)!, instance).ProtocolTool;
			tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(oldFields.Append("calendarPeriod"));
			if (tool.InputSchema.TryGetProperty("required", out var required)) required.EnumerateArray().Should().BeEmpty();
			tool.Annotations!.ReadOnlyHint.Should().BeTrue();
		}
		var lookup = McpServerTool.Create(typeof(PlanningCalendarTools).GetMethod(nameof(PlanningCalendarTools.GetPeriod))!, new PlanningCalendarTools(Substitute.For<IPlanningCalendar>())).ProtocolTool;
		lookup.Name.Should().Be("calendar_period_get");
		lookup.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("period");
		lookup.Annotations!.ReadOnlyHint.Should().BeTrue();
	}

	[Fact]
	public void MixedCalendarAndLegacyRangeOptionsAreRejected()
	{
		Action weekly = () => new WeeklyReportOptions(new DateOnly(2026, 10, 2), CalendarPeriod: "thisWeek").Validate();
		Action reflection = () => new ReflectionReportOptions(14, CalendarPeriod: "lastWeek").Validate();
		Action reflectionEnd = () => new ReflectionReportOptions(EndAtUtc: DateTime.UnixEpoch, CalendarPeriod: "lastWeek").Validate();
		weekly.Should().Throw<ArgumentException>(); reflection.Should().Throw<ArgumentException>(); reflectionEnd.Should().Throw<ArgumentException>();
	}

	private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
	private static DateTime Utc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
