namespace OwnPlanner.Application.Reporting;

/// <summary>
/// Builds a deterministic, read-only reflection report from the current planner state. The host
/// binds the implementation to the authenticated user's database.
/// </summary>
public interface IReflectionReportReader
{
	/// <summary>Builds a bounded report for the requested legacy UTC half-open period or an explicit user-calendar period, keeping focus calendar dates separate from timestamp instants.</summary>
	Task<ReflectionReport> GetAsync(
		ReflectionReportOptions options,
		CancellationToken cancellationToken = default);
}
