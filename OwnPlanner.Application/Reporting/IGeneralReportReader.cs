namespace OwnPlanner.Application.Reporting;

/// <summary>Reads a bounded, read-only current-state report from the host-bound user's planner database.</summary>
public interface IGeneralReportReader
{
	/// <summary>Uses the current UTC date, exact totals and fixed sample limits; accepts no tenant or date overrides.</summary>
	Task<GeneralReport> GetAsync(CancellationToken cancellationToken = default);
	/// <summary>Adds separately labelled local-calendar today and requested-period data, preserving every legacy UTC field.</summary>
	Task<GeneralReport> GetCalendarAsync(string period, CancellationToken cancellationToken = default);
}
