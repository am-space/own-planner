namespace OwnPlanner.Application.Account;

/// <summary>
/// Request body for permanently deleting the authenticated user's account. The current password is
/// required as explicit confirmation of intent and identity.
/// </summary>
/// <param name="Password">The user's current password.</param>
public sealed record DeleteAccountRequest(string Password);
