using FluentAssertions;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class RecurrenceCalculatorTests
{
	private readonly RecurrenceCalculator _calculator = new();

	[Theory]
	[InlineData(DayOfWeek.Monday, "2026-01-02", "2026-01-04", "2026-01-16", "2026-01-18")]
	[InlineData(DayOfWeek.Sunday, "2026-01-02", "2026-01-11", "2026-01-16", "2026-01-25")]
	public void BiweeklyAlignmentUsesStartWeekAndConfiguredWeekStart(DayOfWeek weekStart, params string[] expected)
	{
		var schedule = Weekly("2026-01-01", 2, [DayOfWeek.Friday, DayOfWeek.Sunday], weekStart);
		Dates(schedule, "2026-01-01", "2026-01-26").Should().Equal(expected.Select(DateOnly.Parse));
	}

	[Fact]
	public void EveryThreeWeeksSeeksIntoDistantRangeWithoutReanchoringAtQueryStart()
	{
		var schedule = Weekly("2026-01-01", 3, [DayOfWeek.Monday]);
		Dates(schedule, "2026-02-01", "2026-03-01").Should().Equal(new DateOnly(2026, 2, 9));
	}

	[Fact]
	public void AllWeekdaysExpressDailyWorkWithinWeeklyIntervals()
	{
		var schedule = Weekly("2026-01-01", 2, Enum.GetValues<DayOfWeek>());
		Dates(schedule, "2025-12-29", "2026-01-14").Should().Equal(
			new DateOnly(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3), new(2026, 1, 4),
			new(2026, 1, 12), new(2026, 1, 13));
	}

	[Fact]
	public void SeriesStartAndEndAreInclusiveButQueryEndIsExclusive()
	{
		var schedule = Weekly("2026-11-13", 1, [DayOfWeek.Friday], end: "2026-11-20");
		Dates(schedule, "2026-11-01", "2026-12-01").Should().Equal(new DateOnly(2026, 11, 13), new(2026, 11, 20));
		Dates(schedule, "2026-11-13", "2026-11-20").Should().Equal(new DateOnly(2026, 11, 13));
		Dates(schedule, "2026-11-20", "2026-11-21").Should().Equal(new DateOnly(2026, 11, 20));
		Dates(schedule, "2026-11-21", "2026-12-01").Should().BeEmpty();
		Dates(schedule, "2026-10-01", "2026-11-13").Should().BeEmpty();
	}

	[Theory]
	[InlineData(2024, "2024-02-29")]
	[InlineData(2025, "2025-02-28")]
	[InlineData(2100, "2100-02-28")]
	[InlineData(2000, "2000-02-29")]
	public void MonthlyClampingDeduplicatesWithoutChangingOriginalRule(int year, string february)
	{
		var schedule = Monthly(new DateOnly(year, 1, 1), 1, [29, 30, 31]);
		_calculator.Enumerate(schedule, new(new(year, 2, 1), new(year, 4, 1))).Should().Equal(
			DateOnly.Parse(february), new(year, 3, 29), new(year, 3, 30), new(year, 3, 31));
		schedule.Rule.MonthDays.Should().Equal(29, 30, 31);
	}

	[Fact]
	public void MonthlyIntervalsStayAnchoredToStartMonthAcrossYears()
	{
		var schedule = Monthly(new(2025, 11, 30), 2, [1, 31]);
		Dates(schedule, "2026-01-15", "2026-06-01").Should().Equal(
			new DateOnly(2026, 1, 31), new(2026, 3, 1), new(2026, 3, 31), new(2026, 5, 1), new(2026, 5, 31));
	}

	[Fact]
	public void MonthlyStartDoesNotGenerateEarlierSelectedDatesAndInclusiveEndIncludesClampedDate()
	{
		var schedule = Monthly(new(2026, 1, 30), 1, [1, 31], new(2026, 2, 28));
		Dates(schedule, "2026-01-01", "2026-04-01").Should().Equal(new DateOnly(2026, 1, 31), new(2026, 2, 1), new(2026, 2, 28));
	}

	[Theory]
	[InlineData(RecurrenceFrequency.Weekly)]
	[InlineData(RecurrenceFrequency.Monthly)]
	public void VeryLargeIntervalsDoNotOverflowOrInventNextOccurrences(RecurrenceFrequency frequency)
	{
		var rule = frequency == RecurrenceFrequency.Weekly
			? RecurrenceRule.Weekly(int.MaxValue, [DayOfWeek.Monday], DayOfWeek.Monday)
			: RecurrenceRule.Monthly(int.MaxValue, [1]);
		var schedule = new RecurrenceSchedule(rule, DateOnly.MinValue, null, MissedOccurrencePolicy.SkipExpiredPeriods);
		_calculator.Enumerate(schedule, new(DateOnly.MinValue, DateOnly.MaxValue)).Should().Equal(DateOnly.MinValue);
		_calculator.Enumerate(schedule, new(new(9999, 1, 1), DateOnly.MaxValue)).Should().BeEmpty();
	}

	[Fact]
	public void DateOnlyEdgesDoNotOverflowWeekOrMonthIteration()
	{
		var first = new RecurrenceSchedule(RecurrenceRule.Weekly(1, [DayOfWeek.Monday], DayOfWeek.Sunday),
			DateOnly.MinValue, null, MissedOccurrencePolicy.SkipExpiredPeriods);
		_calculator.Enumerate(first, new(DateOnly.MinValue, new(1, 1, 3))).Should().Equal(DateOnly.MinValue);
		var last = Monthly(new(9999, 12, 1), 1, [30, 31], DateOnly.MaxValue);
		_calculator.Enumerate(last, new(new(9999, 12, 29), DateOnly.MaxValue)).Should().Equal(new DateOnly(9999, 12, 30));
	}

	[Fact]
	public void BoundedQueriesRequireAnOrderedNonemptyRange()
	{
		var date = new DateOnly(2026, 1, 1);
		Action empty = () => new OccurrenceDateRange(date, date);
		Action reverse = () => new OccurrenceDateRange(date, date.AddDays(-1));
		Action missingRange = () => _calculator.Enumerate(Weekly("2026-01-01", 1, [DayOfWeek.Monday]), null!);
		Action missingSchedule = () => _calculator.Enumerate(null!, new(date, date.AddDays(1)));
		empty.Should().Throw<ArgumentException>();
		reverse.Should().Throw<ArgumentException>();
		missingRange.Should().Throw<ArgumentNullException>();
		missingSchedule.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void ReplacingAScheduleLeavesOldFridayObligationAndNewMondayIndependent()
	{
		var oldSchedule = Weekly("2026-11-01", 1, [DayOfWeek.Friday], end: "2026-11-15");
		var nextSchedule = Weekly("2026-11-16", 1, [DayOfWeek.Monday]);
		Dates(oldSchedule, "2026-11-09", "2026-11-23").Should().Equal(new DateOnly(2026, 11, 13));
		Dates(nextSchedule, "2026-11-09", "2026-11-23").Should().Equal(new DateOnly(2026, 11, 16));
	}

	[Fact]
	public void PreservedAdvanceFactOutsideShortenedBoundsIsHistoryNotRegeneratedWork()
	{
		var schedule = Weekly("2026-11-01", 1, [DayOfWeek.Friday], end: "2026-11-15");
		var fact = new TaskOccurrenceFact(new(Guid.NewGuid(), new(2026, 11, 20)), TaskOccurrenceStatus.Completed,
			new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc));
		Dates(schedule, "2026-11-16", "2026-11-23").Should().BeEmpty();
		fact.Id.OccurrenceDate.Should().Be(new DateOnly(2026, 11, 20));
		fact.CompletedAt.Should().Be(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public void ResumptionSchedulesGenerateNoObligationsInBreak()
	{
		var oldSchedule = Weekly("2026-11-01", 1, [DayOfWeek.Friday], end: "2026-11-15");
		var resumed = Weekly("2026-12-07", 1, [DayOfWeek.Friday]);
		Dates(oldSchedule, "2026-11-16", "2026-12-07").Should().BeEmpty();
		Dates(resumed, "2026-11-16", "2026-12-07").Should().BeEmpty();
		Dates(resumed, "2026-12-07", "2026-12-14").Should().Equal(new DateOnly(2026, 12, 11));
	}

	private DateOnly[] Dates(RecurrenceSchedule schedule, string start, string end) =>
		_calculator.Enumerate(schedule, new(DateOnly.Parse(start), DateOnly.Parse(end))).ToArray();

	private static RecurrenceSchedule Weekly(string start, int interval, IEnumerable<DayOfWeek> weekdays,
		DayOfWeek weekStart = DayOfWeek.Monday, string? end = null) =>
		new(RecurrenceRule.Weekly(interval, weekdays, weekStart), DateOnly.Parse(start),
			end is null ? null : DateOnly.Parse(end), MissedOccurrencePolicy.RetainEachObligation);

	private static RecurrenceSchedule Monthly(DateOnly start, int interval, IEnumerable<int> days, DateOnly? end = null) =>
		new(RecurrenceRule.Monthly(interval, days), start, end, MissedOccurrencePolicy.SkipExpiredPeriods);
}
