namespace OwnPlanner.Application.Account;

/// <summary>
/// Builds a portable, self-contained export of the current user's planning data so they can obtain
/// and reuse a complete copy of it (GDPR Art. 15 right of access / Art. 20 data portability).
/// </summary>
public interface IAccountExportService
{
	/// <summary>
	/// Creates a ZIP archive containing a consistent snapshot of the current user's planner database
	/// plus a human-readable README. The snapshot reflects the data at the moment of the call and is
	/// scoped strictly to the requesting user.
	/// </summary>
	/// <param name="cancellationToken">Signals that the caller has lost interest before the archive is built.</param>
	/// <returns>
	/// Details of the generated archive. The <see cref="AccountExport.FilePath"/> points at a
	/// temporary file the caller must delete after streaming the response.
	/// </returns>
	Task<AccountExport> CreateExportAsync(CancellationToken cancellationToken = default);
}
