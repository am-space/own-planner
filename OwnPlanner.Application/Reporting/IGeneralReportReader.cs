namespace OwnPlanner.Application.Reporting;

/// <summary>Reads a bounded, read-only current-state report from the host-bound user's planner database.</summary>
public interface IGeneralReportReader
{
	/// <summary>Uses the current UTC date, exact totals and fixed sample limits; accepts no tenant or date overrides.</summary>
	Task<GeneralReport> GetAsync(CancellationToken cancellationToken = default);
	/// <summary>Adds separately labelled local-calendar data and thisWeek attention, preserving every legacy UTC field.</summary>
	Task<GeneralReport> GetCalendarAsync(string period, CancellationToken cancellationToken = default);
	/// <summary>Reads fresh current-week attention for the host-bound user, with deduplicated overview or all matches in a bounded section page.</summary>
	Task<GeneralAttentionReport> GetAttentionAsync(GeneralAttentionOptions options, CancellationToken cancellationToken = default);
}
