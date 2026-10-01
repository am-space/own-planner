using System.Globalization;
using OwnPlanner.Domain.Goals;

namespace OwnPlanner.Application.WeeklyReviews;

/// <summary>Turns exact task evidence into goal flags and a goals-first review order.</summary>
public static class WeeklyReviewGoalCalculator
{
	public static WeeklyReviewGoals Build(WeeklyReviewGoalData data, WeeklyReviewState review, DateTime nowUtc)
	{
		var zone = TimeZoneInfo.FindSystemTimeZoneById(review.TimeZoneId);
		var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
		var goals = data.Goals.Select(row =>
		{
			var end = PeriodEnd(row);
			var periodFlag = end is null ? null : end < today ? "targetPeriodPassed" :
				end <= today.AddDays(7) ? "targetPeriodEndingSoon" : null;
			return new WeeklyReviewGoal(row.Id, row.Title, row.Metric, row.MetricCurrent,
				row.CompletedLast7Count, row.CompletedLast7, row.OpenTaskCount, row.OpenTasks,
				row.PlannedTaskCount, row.OpenTaskCount == 0,
				(row.LastResumedAt ?? row.CreatedAt) <= nowUtc.AddDays(-14) && row.CompletedLast14Count == 0, periodFlag);
		}).OrderByDescending(goal => goal.NoNextStep || goal.Stalled || goal.TargetPeriodFlag is not null)
			.ThenByDescending(goal => goal.TargetPeriodFlag == "targetPeriodPassed")
			.ThenByDescending(goal => goal.NoNextStep)
			.ThenByDescending(goal => goal.Stalled)
			.ThenBy(goal => goal.Title, StringComparer.OrdinalIgnoreCase).ToArray();
		return new(goals.Length, goals.Count(goal => goal.NoNextStep), goals.Count(goal => goal.Stalled),
			data.CompletedGoalWorkCount, data.CompletedOtherWorkCount, data.PlannedGoalWorkCount,
			data.PlannedOtherWorkCount, goals.Where(goal => goal.PlannedTaskCount == 0).Select(goal => goal.Id).ToArray(), goals);
	}

	private static DateOnly? PeriodEnd(WeeklyReviewGoalRow goal)
	{
		if (goal.Horizon == GoalHorizon.TargetDate)
			return goal.TargetDate is { } date ? DateOnly.FromDateTime(date) : null;
		if (goal.TargetPeriod is not { } period) return null;
		switch (goal.Horizon)
		{
			case GoalHorizon.Monthly when DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month):
				return new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
			case GoalHorizon.Quarterly when period.Length == 7 && period[4] == '-' && period[5] == 'Q' &&
				int.TryParse(period.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var quarterYear) &&
				period[6] is >= '1' and <= '4' && quarterYear is >= 1 and <= 9999:
				var quarter = period[6] - '0';
				var lastMonth = quarter * 3;
				return new DateOnly(quarterYear, lastMonth, DateTime.DaysInMonth(quarterYear, lastMonth));
			case GoalHorizon.Yearly when period.Length == 4 &&
				int.TryParse(period, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1 and <= 9999:
				return new DateOnly(year, 12, 31);
			default: return null;
		}
	}
}
