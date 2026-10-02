using FluentAssertions;
using NSubstitute;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.WeeklyReviews;

namespace OwnPlanner.Application.Tests.Calendar;

public sealed class PlanningCalendarTests
{
	[Theory]
	[InlineData("today", "2026-10-04", "2026-10-05")]
	[InlineData("thisWeek", "2026-09-28", "2026-10-05")]
	[InlineData("remainderOfThisWeek", "2026-10-04", "2026-10-05")]
	[InlineData("nextWeek", "2026-10-05", "2026-10-12")]
	[InlineData("lastWeek", "2026-09-21", "2026-09-28")]
	[InlineData("nextSevenDays", "2026-10-04", "2026-10-11")]
	public void LastDayRemainsInCurrentWeekAndNamedPeriodsAreDistinct(string name, string start, string end)
	{
		var report = PlanningCalendarResolver.Resolve(name, Utc("2026-10-04T12:00:00Z"), "UTC");
		report.StartDate.Should().Be(DateOnly.Parse(start));
		report.EndExclusiveDate.Should().Be(DateOnly.Parse(end));
		report.StartsAtUtc.Should().Be(Utc(start + "T00:00:00Z"));
		report.EndsAtUtc.Should().Be(Utc(end + "T00:00:00Z"));
	}

	[Theory]
	[InlineData("2026-01-01T00:30:00Z", "America/Los_Angeles", 0, "2025-12-31", "2025-12-28", "2026-01-04")]
	[InlineData("2025-12-31T23:30:00Z", "Asia/Tokyo", 1, "2026-01-01", "2025-12-29", "2026-01-05")]
	[InlineData("2026-10-05T00:30:00Z", "UTC", 1, "2026-10-05", "2026-10-05", "2026-10-12")]
	[InlineData("2026-10-04T00:30:00Z", "UTC", 0, "2026-10-04", "2026-10-04", "2026-10-11")]
	public void LocalDatesAndWeekStartsCoverYearAndWeekBoundaries(string clock, string zone, int weekStart, string today, string start, string end)
	{
		var result = PlanningCalendarResolver.Resolve("thisWeek", Utc(clock), zone, weekStart);
		result.Today.Should().Be(DateOnly.Parse(today));
		result.StartDate.Should().Be(DateOnly.Parse(start));
		result.EndExclusiveDate.Should().Be(DateOnly.Parse(end));
		result.FallbackExplanation.Should().BeNull();
	}

	[Theory]
	[InlineData("2026-03-08T12:00:00Z", 167)]
	[InlineData("2026-11-01T12:00:00Z", 169)]
	public void CalendarWeeksUseActualDstDuration(string clock, int hours)
	{
		var result = PlanningCalendarResolver.Resolve("thisWeek", Utc(clock), "America/New_York");
		(result.EndsAtUtc - result.StartsAtUtc).TotalHours.Should().Be(hours);
		(result.FocusEnd - result.FocusStart).TotalHours.Should().Be(168);
	}

	[Theory]
	[InlineData("America/Havana", "2026-03-08", "2026-03-08T05:00:00Z")]
	[InlineData("America/Havana", "2026-11-01", "2026-11-01T04:00:00Z")]
	public void LocalMidnightGapsAndOverlapsUseExistingReviewPolicy(string zone, string date, string expected)
	{
		CalendarRules.ToUtc(DateOnly.Parse(date), TimeOnly.MinValue, TimeZoneInfo.FindSystemTimeZoneById(zone))
			.Should().Be(Utc(expected));
	}

	[Theory]
	[InlineData("2026-03-09T12:00:00Z", "2026-03-02", "2026-03-10")]
	[InlineData("2026-10-05T04:00:00Z", "2026-09-28", "2026-10-05")]
	public void RollingLastSevenDaysUses168HoursAndTheLocalDatesTouched(string clock, string start, string end)
	{
		var result = PlanningCalendarResolver.Resolve("lastSevenDays", Utc(clock), "America/New_York");
		result.IsRolling.Should().BeTrue();
		result.EndsAtUtc.Should().Be(Utc(clock));
		(result.EndsAtUtc - result.StartsAtUtc).TotalHours.Should().Be(168);
		result.StartDate.Should().Be(DateOnly.Parse(start));
		result.EndExclusiveDate.Should().Be(DateOnly.Parse(end));
	}

	[Fact]
	public async Task DisabledRemindersStillSupplyPreferencesAndEveryCallUsesFreshClock()
	{
		var ct = TestContext.Current.CancellationToken;
		var store = Substitute.For<IWeeklyReviewStore>();
		store.GetPreferencesAsync(ct).Returns(new WeeklyReviewPreferences { Enabled = false, TimeZoneId = "Asia/Tokyo", WeekStart = 0 });
		var clock = new MutableClock { Now = Utc("2026-10-02T14:59:00Z") };
		var service = new PlanningCalendar(store, clock);
		var before = await service.ResolveAsync("today", ct);
		clock.Now = clock.Now.AddMinutes(2);
		var after = await service.ResolveAsync("today", ct);
		before.Today.Should().Be(new DateOnly(2026, 10, 2));
		after.Today.Should().Be(new DateOnly(2026, 10, 3));
		after.WeekStart.Should().Be(0);
		after.FallbackExplanation.Should().BeNull();
		await store.Received(2).GetPreferencesAsync(ct);
		store.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(IWeeklyReviewStore.GetPreferencesAsync));
	}

	[Fact]
	public void MissingPreferencesDiscloseDeterministicFallback()
	{
		var result = PlanningCalendarResolver.Resolve("thisWeek", Utc("2026-10-04T12:00:00Z"), null);
		result.TimeZone.Should().Be("UTC");
		result.WeekStart.Should().Be(1);
		result.FallbackExplanation.Should().Contain("No timezone is configured").And.Contain("UTC").And.Contain("Monday");
	}

	[Theory]
	[InlineData("last week")]
	[InlineData("")]
	public void UnknownPeriodsFailExplicitly(string period)
	{
		Action action = () => PlanningCalendarResolver.Resolve(period, DateTime.UnixEpoch, "UTC");
		action.Should().Throw<ArgumentException>();
	}

	private static DateTime Utc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
	private sealed class MutableClock : TimeProvider
	{
		public DateTime Now { get; set; }
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
}
