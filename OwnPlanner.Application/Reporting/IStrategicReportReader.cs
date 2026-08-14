namespace OwnPlanner.Application.Reporting;

/// <summary>
/// Builds a deterministic, read-only strategic snapshot of the current planner database.
/// The host is responsible for binding the implementation to the authenticated user's data.
/// </summary>
public interface IStrategicReportReader
{
	/// <summary>Builds a strategic report using the supplied bounded sample limits.</summary>
	Task<StrategicReport> GetAsync(
		StrategicReportOptions options,
		CancellationToken cancellationToken = default);
}
