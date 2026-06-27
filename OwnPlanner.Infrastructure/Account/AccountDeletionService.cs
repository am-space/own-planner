using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Account;
using OwnPlanner.Application.Auth;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Account;

/// <summary>
/// Permanently erases a user's account across both data stores. The auth record is removed first —
/// its <see cref="AuthDbContext"/> foreign keys cascade-delete the user's access tokens,
/// password-reset tokens, daily usage counters, and quota overrides — which immediately makes login
/// impossible. The per-user planner database file is then deleted. Any file left behind by a failure
/// is orphaned and unreachable, since no surviving account can resolve it.
/// </summary>
public sealed class AccountDeletionService(
	IUserRepository userRepository,
	IAuthService authService,
	IPlannerDbContextFactory dbContextFactory,
	ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
	public async Task<AccountDeletionResult> DeleteAccountAsync(Guid userId, string password, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(password))
		{
			return new AccountDeletionResult(false, "Password is required.");
		}

		var user = await userRepository.GetByIdAsync(userId, cancellationToken);
		if (user is null)
		{
			return new AccountDeletionResult(false, "Account not found.");
		}

		if (!authService.VerifyPassword(password, user.PasswordHash))
		{
			logger.LogWarning("Account deletion rejected: password verification failed for user {UserId}", userId);
			return new AccountDeletionResult(false, "Password is incorrect.");
		}

		// Remove the auth record (cascades all dependent auth data) before touching the planner DB.
		await userRepository.DeleteAsync(userId, cancellationToken);
		logger.LogInformation("Deleted auth records for user {UserId}", userId);

		// Delete the per-user planner database file. Best-effort: a leftover file is unreachable.
		await dbContextFactory.DeleteUserDatabaseAsync(userId.ToString(), cancellationToken);
		logger.LogInformation("Deleted planner database for user {UserId}", userId);

		return new AccountDeletionResult(true);
	}
}
