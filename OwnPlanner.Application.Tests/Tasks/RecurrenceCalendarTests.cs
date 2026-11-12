using System.Globalization;
using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Tasks;

namespace OwnPlanner.Application.Tests.Tasks;

public sealed class RecurrenceCalendarTests
{
	[Fact]
	public void SundayPlanningShowsBothWeeksWithoutExpiringFriday()
	{
		var calendar = Calendar("2026-11-15T18:00:00Z");
		var window = RecurrenceCalendar.CurrentAndNext(RecurrenceFrequency.Weekly, calendar);
		var schedule = new RecurrenceSchedule(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			new(2026, 11, 1), null, MissedOccurrencePolicy.SkipExpiredPeriods);
		var occurrences = new RecurrenceCalculator().Enumerate(schedule, window).ToArray();
		window.Should().Be(new OccurrenceDateRange(new(2026, 11, 9), new(2026, 11, 23)));
		occurrences.Should().Equal(new DateOnly(2026, 11, 13), new(2026, 11, 20));
		RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, occurrences[0], calendar)
			.IsExpired(calendar.AsOfUtc).Should().BeFalse();
	}

	[Theory]
	[InlineData("2026-12-31T12:00:00Z", "2026-12-01", "2027-02-01")]
	[InlineData("2024-02-29T12:00:00Z", "2024-02-01", "2024-04-01")]
	public void CurrentAndNextMonthsUseCalendarArithmetic(string now, string start, string end)
	{
		var calendar = Calendar(now);
		var range = RecurrenceCalendar.CurrentAndNext(RecurrenceFrequency.Monthly, calendar);
		range.StartDate.Should().Be(DateOnly.Parse(start));
		range.EndExclusiveDate.Should().Be(DateOnly.Parse(end));
		var schedule = new RecurrenceSchedule(RecurrenceRule.Monthly(1, [31]), range.StartDate, null, MissedOccurrencePolicy.SkipExpiredPeriods);
		new RecurrenceCalculator().Enumerate(schedule, range).Should().HaveCount(2);
	}

	[Theory]
	[InlineData(0, "2026-11-08", "2026-11-15")]
	[InlineData(1, "2026-11-09", "2026-11-16")]
	[InlineData(6, "2026-11-07", "2026-11-14")]
	public void ExpiryUsesConfiguredWeekStartAndWaitsForExactBoundary(int weekStart, string start, string end)
	{
		var calendar = Calendar("2026-11-13T12:00:00Z", weekStart: weekStart);
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, new(2026, 11, 13), calendar);
		period.Dates.StartDate.Should().Be(DateOnly.Parse(start));
		period.Dates.EndExclusiveDate.Should().Be(DateOnly.Parse(end));
		period.IsExpired(period.EndsAtUtc.AddTicks(-1)).Should().BeFalse();
		period.IsExpired(period.EndsAtUtc).Should().BeTrue();
	}

	[Theory]
	[InlineData("2026-03-08T12:00:00Z", "2026-03-06", "2026-03-09T04:00:00Z", 167)]
	[InlineData("2026-11-01T12:00:00Z", "2026-10-30", "2026-11-02T05:00:00Z", 169)]
	public void WeeklyExpiryUsesActualDstPeriodRatherThan168Hours(string now, string scheduled, string endUtc, int hours)
	{
		var calendar = Calendar(now, "America/New_York");
		var date = DateOnly.Parse(scheduled);
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, date, calendar);
		period.EndsAtUtc.Should().Be(Utc(endUtc));
		(period.EndsAtUtc - period.StartsAtUtc).TotalHours.Should().Be(hours);
		period.IsExpired(calendar.AsOfUtc).Should().BeFalse();
		period.Dates.StartDate.Should().BeOnOrBefore(date);
		period.Dates.EndExclusiveDate.Should().BeAfter(date);
	}

	[Theory]
	[InlineData("2026-03-15T12:00:00Z", "2026-04-01T04:00:00Z", 743)]
	[InlineData("2026-11-15T12:00:00Z", "2026-12-01T05:00:00Z", 721)]
	public void MonthlyExpiryAlsoUsesActualDstBoundaries(string now, string endUtc, int hours)
	{
		var calendar = Calendar(now, "America/New_York");
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Monthly, calendar.Today, calendar);
		period.EndsAtUtc.Should().Be(Utc(endUtc));
		(period.EndsAtUtc - period.StartsAtUtc).TotalHours.Should().Be(hours);
		period.IsExpired(period.EndsAtUtc.AddTicks(-1)).Should().BeFalse();
		period.IsExpired(period.EndsAtUtc).Should().BeTrue();
	}

	[Theory]
	[InlineData("2026-03-07T12:00:00Z", "2026-03-07", "2026-03-08T05:00:00Z")]
	[InlineData("2026-10-31T12:00:00Z", "2026-10-31", "2026-11-01T04:00:00Z")]
	public void MidnightGapAndOverlapUseSharedCalendarBoundaryPolicy(string now, string scheduled, string expected)
	{
		var calendar = Calendar(now, "America/Havana", weekStart: 0);
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, DateOnly.Parse(scheduled), calendar);
		period.EndsAtUtc.Should().Be(Utc(expected));
	}

	[Theory]
	[InlineData("2026-01-01T00:30:00Z", "America/Los_Angeles", "2025-12-01", "2026-01-01T08:00:00Z")]
	[InlineData("2025-12-31T23:30:00Z", "Asia/Tokyo", "2026-01-01", "2026-01-31T15:00:00Z")]
	public void MonthlyPeriodsUseLocalCalendarAtYearBoundary(string now, string zone, string monthStart, string endUtc)
	{
		var calendar = Calendar(now, zone);
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Monthly, calendar.Today, calendar);
		period.Dates.StartDate.Should().Be(DateOnly.Parse(monthStart));
		period.EndsAtUtc.Should().Be(Utc(endUtc));
		period.IsExpired(calendar.AsOfUtc).Should().BeFalse();
	}

	[Fact]
	public void ShortenedSeriesEndDoesNotPrematurelyExpireTheOriginalWeek()
	{
		var friday = new DateOnly(2026, 11, 13);
		var shortenedEnd = new DateOnly(2026, 11, 13);
		var calendar = Calendar("2026-11-14T12:00:00Z");
		var schedule = new RecurrenceSchedule(RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			new(2026, 11, 1), shortenedEnd, MissedOccurrencePolicy.SkipExpiredPeriods);
		new RecurrenceCalculator().Enumerate(schedule, new(new(2026, 11, 9), new(2026, 11, 16)))
			.Should().Equal(friday);
		RecurrenceCalendar.PeriodContaining(schedule.Rule.Frequency, friday, calendar)
			.IsExpired(calendar.AsOfUtc).Should().BeFalse();
	}

	[Fact]
	public void PreferenceChangesKeepScheduledDatesButResolveUnfinishedPeriodsAgainstCurrentCalendar()
	{
		var rule = RecurrenceRule.Weekly(2, [DayOfWeek.Sunday], DayOfWeek.Monday);
		var schedule = new RecurrenceSchedule(rule, new(2026, 11, 9), null, MissedOccurrencePolicy.RetainEachObligation);
		var range = new OccurrenceDateRange(new(2026, 11, 9), new(2026, 12, 1));
		var calculator = new RecurrenceCalculator();
		var dates = calculator.Enumerate(schedule, range).ToArray();
		var before = Calendar("2026-11-15T18:00:00Z", "UTC", weekStart: 1);
		var after = Calendar("2026-11-15T18:00:00Z", "Asia/Tokyo", weekStart: 0);
		var originalPeriod = RecurrenceCalendar.PeriodContaining(rule.Frequency, dates[0], before);
		var currentPeriod = RecurrenceCalendar.PeriodContaining(rule.Frequency, dates[0], after);
		dates.Should().Equal(new DateOnly(2026, 11, 15), new(2026, 11, 29));
		calculator.Enumerate(schedule, range).Should().Equal(dates);
		originalPeriod.Dates.EndExclusiveDate.Should().Be(new DateOnly(2026, 11, 16));
		currentPeriod.Dates.EndExclusiveDate.Should().Be(new DateOnly(2026, 11, 22));
		rule.WeekStart.Should().Be(DayOfWeek.Monday);
	}

	[Fact]
	public async Task SharedCalendarFallbackAndDisabledRemindersStayReadOnly()
	{
		var ct = TestContext.Current.CancellationToken;
		var store = Substitute.For<IWeeklyReviewStore>();
		store.GetPreferencesAsync(ct).Returns(new WeeklyReviewPreferences { Enabled = false, TimeZoneId = null, WeekStart = 0 });
		var clock = new FixedClock();
		var calendar = await new PlanningCalendar(store, clock).ResolveAsync("today", ct);
		var range = RecurrenceCalendar.CurrentAndNext(RecurrenceFrequency.Weekly, calendar);
		RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, calendar.Today, calendar).IsExpired(calendar.AsOfUtc).Should().BeFalse();
		range.StartDate.Should().Be(new DateOnly(2026, 11, 15));
		calendar.FallbackExplanation.Should().Contain("UTC");
		store.ReceivedCalls().Should().ContainSingle(call => call.GetMethodInfo().Name == nameof(IWeeklyReviewStore.GetPreferencesAsync));
		store.ReceivedCalls().Should().HaveCount(1);
	}

	[Fact]
	public void InvalidFrequencyAndNonUtcExpiryInstantAreRejected()
	{
		var calendar = Calendar("2026-11-15T18:00:00Z");
		Action invalidRange = () => RecurrenceCalendar.CurrentAndNext((RecurrenceFrequency)99, calendar);
		Action invalidPeriod = () => RecurrenceCalendar.PeriodContaining((RecurrenceFrequency)99, calendar.Today, calendar);
		var period = RecurrenceCalendar.PeriodContaining(RecurrenceFrequency.Weekly, calendar.Today, calendar);
		Action local = () => period.IsExpired(new(2026, 11, 15, 0, 0, 0, DateTimeKind.Local));
		invalidRange.Should().Throw<ArgumentOutOfRangeException>();
		invalidPeriod.Should().Throw<ArgumentOutOfRangeException>();
		local.Should().Throw<ArgumentException>();
	}

	private static PlanningPeriod Calendar(string now, string? zone = "UTC", int weekStart = 1) =>
		PlanningCalendarResolver.Resolve("today", Utc(now), zone, weekStart);

	private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture,
		DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

	private sealed class FixedClock : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => new(Utc("2026-11-15T18:00:00Z"));
	}
}
