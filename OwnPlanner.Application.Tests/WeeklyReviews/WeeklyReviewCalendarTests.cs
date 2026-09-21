using FluentAssertions;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.WeeklyReviews;

namespace OwnPlanner.Application.Tests.WeeklyReviews;

public sealed class WeeklyReviewCalendarTests
{
	[Theory]
	[InlineData("2026-09-20T18:00:00Z", "UTC", 1, "2026-09-21")]
	[InlineData("2026-09-21T01:00:00Z", "America/Los_Angeles", 1, "2026-09-21")]
	[InlineData("2026-12-31T12:00:00Z", "UTC", 1, "2026-12-28")]
	[InlineData("2027-01-02T12:00:00Z", "UTC", 0, "2027-01-03")]
	[InlineData("2026-09-20T12:00:00Z", "Pacific/Auckland", 1, "2026-09-21")]
	public void ManualReview_UsesLocalCalendarAndLastDayTargetsNextWeek(string now, string zone, int weekStart, string target)
	{
		var review = WeeklyReviewCalendar.Create(new WeeklyReviewPreferences { TimeZoneId = zone, WeekStart = weekStart },
			DateTime.Parse(now).ToUniversalTime(), false);
		review.TargetWeek.Should().Be(DateOnly.Parse(target));
	}

	[Theory]
	[InlineData("2026-03-08", "02:30", "2026-03-08T07:00:00Z")]
	[InlineData("2026-11-01", "01:30", "2026-11-01T05:30:00Z")]
	public void DstGapAdvancesToFirstValidMinute_OverlapUsesEarlierInstant(string date, string time, string expected)
	{
		WeeklyReviewCalendar.ToUtc(DateOnly.Parse(date), TimeOnly.Parse(time), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))
			.Should().Be(DateTime.Parse(expected).ToUniversalTime());
	}

	[Fact]
	public void DstWeekHas167Hours_AndNoTimezoneIsGuessed()
	{
		var preferences = new WeeklyReviewPreferences { TimeZoneId = "America/New_York" };
		var review = WeeklyReviewCalendar.Create(preferences, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc), false);
		(review.EndsAtUtc - review.StartsAtUtc).TotalHours.Should().Be(167);
		var missing = () => WeeklyReviewCalendar.Create(new(), DateTime.UtcNow, false);
		missing.Should().Throw<ArgumentException>().WithMessage("*timezone*");
	}

	[Theory]
	[InlineData(null, 1, "18:00", "telegram")]
	[InlineData("Not/AZone", 1, "18:00", "telegram")]
	[InlineData("UTC", 7, "18:00", "telegram")]
	[InlineData("UTC", 1, "25:00", "telegram")]
	[InlineData("UTC", 1, "18:00", "email")]
	public void InvalidOptInIsRejected(string? zone, int start, string time, string channel)
	{
		var action = () => WeeklyReviewCalendar.Validate(zone, start, time, channel, true);
		action.Should().Throw<ArgumentException>();
	}
}
