namespace OwnPlanner.Domain.Users;

/// <summary>
/// Represents a user in the system.
/// </summary>
public class User
{
	public Guid Id { get; private set; }
	public string Email { get; private set; } = string.Empty;
	public string Username { get; private set; } = string.Empty;
	public string PasswordHash { get; private set; } = string.Empty;
	public DateTime CreatedAt { get; private set; }
	public DateTime UpdatedAt { get; private set; }
	public DateTime? LastLoginAt { get; private set; }
	public bool IsActive { get; private set; }

	// EF Core constructor
	private User() { }

	public User(string email, string username, string passwordHash)
	{
		Id = Guid.NewGuid();
		SetEmail(email);
		SetUsername(username);
		SetPasswordHash(passwordHash);
		var now = DateTime.UtcNow;
		CreatedAt = now;
		UpdatedAt = now;
		IsActive = true;
	}

	public void SetEmail(string email)
	{
		if (string.IsNullOrWhiteSpace(email))
			throw new ArgumentException("Email is required", nameof(email));

		if (!IsValidEmail(email))
			throw new ArgumentException("Invalid email format", nameof(email));

		Email = email.Trim().ToLowerInvariant();
		Touch();
	}

	public void SetUsername(string username)
	{
		if (string.IsNullOrWhiteSpace(username))
			throw new ArgumentException("Username is required", nameof(username));

		if (username.Length < 3)
			throw new ArgumentException("Username must be at least 3 characters", nameof(username));

		if (username.Length > 50)
			throw new ArgumentException("Username must not exceed 50 characters", nameof(username));

		Username = username.Trim();
		Touch();
	}

	public void SetPasswordHash(string passwordHash)
	{
		if (string.IsNullOrWhiteSpace(passwordHash))
			throw new ArgumentException("Password hash is required", nameof(passwordHash));

		PasswordHash = passwordHash;
		Touch();
	}

	public void RecordLogin()
	{
		LastLoginAt = DateTime.UtcNow;
		Touch();
	}

	public void Deactivate()
	{
		IsActive = false;
		Touch();
	}

	public void Activate()
	{
		IsActive = true;
		Touch();
	}

	private void Touch() => UpdatedAt = DateTime.UtcNow;

	private static bool IsValidEmail(string email)
	{
		try
		{
			var addr = new System.Net.Mail.MailAddress(email);
			return addr.Address == email;
		}
		catch
		{
			return false;
		}
	}
}
