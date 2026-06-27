namespace OwnPlanner.Application.Account;

/// <summary>
/// Permanently deletes a user's account and all associated personal data (GDPR Art. 17 right to
/// erasure). OwnPlanner retains no data for legal or accounting reasons, so deletion is total.
/// </summary>
public interface IAccountDeletionService
{
	/// <summary>
	/// Verifies the supplied password and, on success, permanently erases the user's account: their
	/// auth record and all cascaded auth data (access tokens, password-reset tokens, usage counters,
	/// quota overrides) plus their entire per-user planner database. The operation is irreversible.
	/// </summary>
	/// <param name="userId">The authenticated user to delete.</param>
	/// <param name="password">The user's current password, required as confirmation.</param>
	/// <param name="cancellationToken">Signals that the caller has lost interest before deletion completes.</param>
	/// <returns>A success result, or a failure result (e.g. wrong password) that erases nothing.</returns>
	Task<AccountDeletionResult> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken = default);
}
