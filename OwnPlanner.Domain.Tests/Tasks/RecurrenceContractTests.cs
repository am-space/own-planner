using FluentAssertions;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Domain.Tests.Tasks;

public sealed class RecurrenceContractTests
{
	[Fact]
	public void EquivalentNormalizedRulesHaveValueEqualityIncludingTheirDaySelections()
	{
		var first = RecurrenceRule.Weekly(2, [DayOfWeek.Monday, DayOfWeek.Friday], DayOfWeek.Monday);
		var same = RecurrenceRule.Weekly(2, [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Friday], DayOfWeek.Monday);
		first.Should().Be(same);
		first.GetHashCode().Should().Be(same.GetHashCode());
		first.Should().NotBe(RecurrenceRule.Weekly(2, [DayOfWeek.Monday], DayOfWeek.Monday));
		first.Should().NotBe(RecurrenceRule.Weekly(1, [DayOfWeek.Monday, DayOfWeek.Friday], DayOfWeek.Monday));
		first.Should().NotBe(RecurrenceRule.Weekly(2, [DayOfWeek.Monday, DayOfWeek.Friday], DayOfWeek.Sunday));
		var monthly = RecurrenceRule.Monthly(1, [31, 29, 31]);
		var sameMonthly = RecurrenceRule.Monthly(1, [29, 31]);
		monthly.Should().Be(sameMonthly);
		monthly.GetHashCode().Should().Be(sameMonthly.GetHashCode());
		monthly.Should().NotBe(RecurrenceRule.Monthly(1, [29, 30, 31]));
	}

	[Fact]
	public void WeeklyRuleCopiesNormalizesAndProtectsSelectedDays()
	{
		var input = new[] { DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Monday };
		var rule = RecurrenceRule.Weekly(2, input, DayOfWeek.Sunday);
		input[0] = DayOfWeek.Friday;

		rule.Frequency.Should().Be(RecurrenceFrequency.Weekly);
		rule.Interval.Should().Be(2);
		rule.WeekStart.Should().Be(DayOfWeek.Sunday);
		rule.Weekdays.Should().Equal(DayOfWeek.Sunday, DayOfWeek.Monday);
		rule.MonthDays.Should().BeEmpty();
		Action mutate = () => ((IList<DayOfWeek>)rule.Weekdays)[0] = DayOfWeek.Friday;
		mutate.Should().Throw<NotSupportedException>();
	}

	[Fact]
	public void MonthlyRulePreservesOriginalDaysForClampingInEachMonth()
	{
		var input = new[] { 31, 29, 30, 31 };
		var rule = RecurrenceRule.Monthly(3, input);
		input[0] = 1;
		rule.MonthDays.Should().Equal(29, 30, 31);
		rule.Frequency.Should().Be(RecurrenceFrequency.Monthly);
		rule.Interval.Should().Be(3);
		rule.Weekdays.Should().BeEmpty();
		Action mutate = () => ((IList<int>)rule.MonthDays)[0] = 1;
		mutate.Should().Throw<NotSupportedException>();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void NonpositiveIntervalsAreRejected(int interval)
	{
		Action weekly = () => RecurrenceRule.Weekly(interval, [DayOfWeek.Monday], DayOfWeek.Monday);
		Action monthly = () => RecurrenceRule.Monthly(interval, [1]);
		weekly.Should().Throw<ArgumentOutOfRangeException>();
		monthly.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void EmptyNullAndUndefinedWeekdaysAreRejected()
	{
		Action empty = () => RecurrenceRule.Weekly(1, [], DayOfWeek.Monday);
		Action missing = () => RecurrenceRule.Weekly(1, null!, DayOfWeek.Monday);
		Action invalidDay = () => RecurrenceRule.Weekly(1, [(DayOfWeek)7], DayOfWeek.Monday);
		Action invalidStart = () => RecurrenceRule.Weekly(1, [DayOfWeek.Monday], (DayOfWeek)(-1));
		empty.Should().Throw<ArgumentException>();
		missing.Should().Throw<ArgumentNullException>();
		invalidDay.Should().Throw<ArgumentException>();
		invalidStart.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(32)]
	[InlineData(-1)]
	public void InvalidMonthDaysAreRejected(int day)
	{
		Action action = () => RecurrenceRule.Monthly(1, [1, day]);
		action.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void EmptyAndNullMonthDaysAreRejected()
	{
		Action empty = () => RecurrenceRule.Monthly(1, []);
		Action missing = () => RecurrenceRule.Monthly(1, null!);
		empty.Should().Throw<ArgumentException>();
		missing.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void ScheduleRequiresRuleValidBoundsAndExplicitPolicy()
	{
		var rule = RecurrenceRule.Monthly(1, [31]);
		var start = new DateOnly(2026, 1, 31);
		Action missing = () => new RecurrenceSchedule(null!, start, null, MissedOccurrencePolicy.SkipExpiredPeriods);
		Action backwards = () => new RecurrenceSchedule(rule, start, start.AddDays(-1), MissedOccurrencePolicy.SkipExpiredPeriods);
		Action policy = () => new RecurrenceSchedule(rule, start, null, (MissedOccurrencePolicy)99);
		missing.Should().Throw<ArgumentNullException>();
		backwards.Should().Throw<ArgumentException>();
		policy.Should().Throw<ArgumentOutOfRangeException>();
		new RecurrenceSchedule(rule, start, start, MissedOccurrencePolicy.RetainEachObligation).EndDate.Should().Be(start);
	}

	[Fact]
	public void OccurrenceIdentityUsesOriginalTaskAndDateWithValueEquality()
	{
		var id = Guid.NewGuid();
		var date = new DateOnly(2026, 11, 13);
		new TaskOccurrenceId(id, date).Should().Be(new TaskOccurrenceId(id, date));
		new TaskOccurrenceId(id, date).Should().NotBe(new TaskOccurrenceId(id, date.AddDays(1)));
		Action empty = () => new TaskOccurrenceId(Guid.Empty, date);
		empty.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData(TaskOccurrenceStatus.Skipped)]
	[InlineData(TaskOccurrenceStatus.Cancelled)]
	public void UncompletedFactsCannotHaveCompletionInstants(TaskOccurrenceStatus status)
	{
		var id = new TaskOccurrenceId(Guid.NewGuid(), new DateOnly(2026, 11, 13));
		new TaskOccurrenceFact(id, status).CompletedAt.Should().BeNull();
		Action invalid = () => new TaskOccurrenceFact(id, status, DateTime.UnixEpoch);
		invalid.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData(DateTimeKind.Local)]
	[InlineData(DateTimeKind.Unspecified)]
	public void CompletionRequiresActualUtcInstant(DateTimeKind kind)
	{
		var id = new TaskOccurrenceId(Guid.NewGuid(), new DateOnly(2026, 11, 13));
		Action missing = () => new TaskOccurrenceFact(id, TaskOccurrenceStatus.Completed);
		Action nonUtc = () => new TaskOccurrenceFact(id, TaskOccurrenceStatus.Completed, new DateTime(2026, 11, 16, 9, 0, 0, kind));
		missing.Should().Throw<ArgumentException>();
		nonUtc.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void CompletionFactRetainsScheduledDateAndIndependentActualCompletionTime()
	{
		var id = new TaskOccurrenceId(Guid.NewGuid(), new DateOnly(2026, 11, 13));
		var monday = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
		var fact = new TaskOccurrenceFact(id, TaskOccurrenceStatus.Completed, monday);
		fact.Id.Should().Be(id);
		fact.CompletedAt.Should().Be(monday);
		Action invalidStatus = () => new TaskOccurrenceFact(id, (TaskOccurrenceStatus)99);
		Action missingId = () => new TaskOccurrenceFact(null!, TaskOccurrenceStatus.Skipped);
		invalidStatus.Should().Throw<ArgumentOutOfRangeException>();
		missingId.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void SuccessorLinkageDoesNotReidentifyHistoricalFacts()
	{
		var predecessor = Guid.NewGuid();
		var successor = Guid.NewGuid();
		var schedule = new RecurrenceSchedule(RecurrenceRule.Monthly(1, [15]), new(2026, 12, 1), null,
			MissedOccurrencePolicy.RetainEachObligation);
		var series = new RecurringSeries(successor, schedule, predecessorTaskId: predecessor);
		var fact = new TaskOccurrenceFact(new(predecessor, new(2026, 11, 15)), TaskOccurrenceStatus.Completed, DateTime.UnixEpoch);
		series.PredecessorTaskId.Should().Be(predecessor);
		fact.Id.TaskId.Should().Be(predecessor);
		fact.Id.TaskId.Should().NotBe(series.TaskId);
		Action selfLink = () => new RecurringSeries(successor, schedule, predecessorTaskId: successor);
		Action cycle = () => new RecurringSeries(successor, schedule, predecessorTaskId: predecessor, successorTaskId: predecessor);
		Action missingId = () => new RecurringSeries(Guid.Empty, schedule);
		selfLink.Should().Throw<ArgumentException>();
		cycle.Should().Throw<ArgumentException>();
		missingId.Should().Throw<ArgumentException>();
	}
}
