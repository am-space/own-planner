namespace OwnPlanner.Domain.Users;

/// <summary>
/// Represents a server-managed personal access token for MCP access.
/// </summary>
public class PersonalAccessToken : EntityBase
{
	public Guid UserId { get; private set; }
	public string Name { get; private set; } = string.Empty;
	public string TokenHash { get; private set; } = string.Empty;
	public DateTime? LastUsedAt { get; private set; }
	public DateTime? RevokedAt { get; private set; }

	// EF Core constructor
	private PersonalAccessToken() { }

	public PersonalAccessToken(Guid userId, string name, string tokenHash)
		: base(Guid.NewGuid())
	{
		SetUserId(userId);
		SetName(name);
		SetTokenHash(tokenHash);
	}

	public void RecordUsage()
	{
		LastUsedAt = DateTime.UtcNow;
		Touch();
	}

	public void Revoke()
	{
		RevokedAt = DateTime.UtcNow;
		Touch();
	}

	private void SetUserId(Guid userId)
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		UserId = userId;
		Touch();
	}

	private void SetName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Token name is required", nameof(name));

		if (name.Length > 100)
			throw new ArgumentException("Token name must not exceed 100 characters", nameof(name));

		Name = name.Trim();
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
