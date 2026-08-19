namespace OwnPlanner.Application.Reporting;

/// <summary>
/// Builds a deterministic, read-only seven-day workload report. The host binds the implementation
/// to the authenticated user's planner database.
/// </summary>
public interface IWeeklyReportReader
{
	/// <summary>Builds the weekly report using the supplied UTC date window and bounded options.</summary>
	Task<WeeklyReport> GetAsync(
		WeeklyReportOptions options,
		CancellationToken cancellationToken = default);
}
