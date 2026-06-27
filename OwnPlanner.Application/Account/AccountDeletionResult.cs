namespace OwnPlanner.Application.Account;

/// <summary>
/// The outcome of an account deletion attempt.
/// </summary>
/// <param name="Success">Whether the account and its data were permanently deleted.</param>
/// <param name="ErrorMessage">A user-facing reason when <paramref name="Success"/> is <c>false</c>.</param>
public sealed record AccountDeletionResult(bool Success, string? ErrorMessage = null);
