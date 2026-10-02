namespace OwnPlanner.Application.Reporting;

/// <summary>
/// Builds a deterministic, read-only workload report for a legacy seven-day UTC window or an explicitly requested user-calendar period. The host binds the implementation
/// to the authenticated user's planner database.
/// </summary>
public interface IWeeklyReportReader
{
	/// <summary>Builds the weekly report using the supplied UTC date window or named user-calendar period and bounded options.</summary>
	Task<WeeklyReport> GetAsync(
		WeeklyReportOptions options,
		CancellationToken cancellationToken = default);
}
