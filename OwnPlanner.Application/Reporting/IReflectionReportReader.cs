namespace OwnPlanner.Application.Reporting;

/// <summary>
/// Builds a deterministic, read-only reflection report from the current planner state. The host
/// binds the implementation to the authenticated user's database.
/// </summary>
public interface IReflectionReportReader
{
	/// <summary>Builds a bounded report for the requested UTC half-open period.</summary>
	Task<ReflectionReport> GetAsync(
		ReflectionReportOptions options,
		CancellationToken cancellationToken = default);
}
