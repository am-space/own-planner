namespace OwnPlanner.Domain.Users;

/// <summary>
/// Represents a single-use, time-limited password-reset token. Only the hash of the
/// token value is stored; the plaintext is emailed to the user and never persisted.
/// </summary>
public class PasswordResetToken : EntityBase
{
	public Guid UserId { get; private set; }
	public string TokenHash { get; private set; } = string.Empty;
	public DateTime ExpiresAt { get; private set; }
	public DateTime? ConsumedAt { get; private set; }

	// EF Core constructor
	private PasswordResetToken() { }

	public PasswordResetToken(Guid userId, string tokenHash, DateTime expiresAt)
		: base(Guid.NewGuid())
	{
		SetUserId(userId);
		SetTokenHash(tokenHash);
		ExpiresAt = expiresAt;
	}

	/// <summary>Returns <c>true</c> when the token has not been consumed and has not expired.</summary>
	public bool IsActive(DateTime now) => ConsumedAt is null && ExpiresAt > now;

	/// <summary>Marks the token as used so it cannot be redeemed again.</summary>
	public void Consume()
	{
		ConsumedAt = DateTime.UtcNow;
		Touch();
	}

	private void SetUserId(Guid userId)
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		UserId = userId;
		Touch();
	}

	private void SetTokenHash(string tokenHash)
	{
		if (string.IsNullOrWhiteSpace(tokenHash))
			throw new ArgumentException("Token hash is required", nameof(tokenHash));

		TokenHash = tokenHash;
		Touch();
	}
}
