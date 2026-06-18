namespace OwnPlanner.Application.Email;

/// <summary>
/// Email delivery configuration. Bound from the "Email" configuration section.
/// Secrets (<see cref="Host"/>, <see cref="User"/>, <see cref="Password"/>, <see cref="FromAddress"/>)
/// should be supplied via user-secrets or environment variables — never committed.
/// </summary>
public sealed class EmailOptions
{
	/// <summary>Which <see cref="IEmailSender"/> to use: "Smtp" or "Logging".</summary>
	public string Provider { get; set; } = "Logging";

	/// <summary>SMTP server host name.</summary>
	public string Host { get; set; } = string.Empty;

	/// <summary>SMTP server port. 587 is STARTTLS submission.</summary>
	public int Port { get; set; } = 587;

	/// <summary>SMTP authentication user name.</summary>
	public string User { get; set; } = string.Empty;

	/// <summary>SMTP authentication password.</summary>
	public string Password { get; set; } = string.Empty;

	/// <summary>Address that outbound mail is sent from.</summary>
	public string FromAddress { get; set; } = string.Empty;

	/// <summary>Display name shown on outbound mail.</summary>
	public string FromName { get; set; } = "OwnPlanner";

	/// <summary>Base URL the password-reset link is built from, e.g. "https://controlcode.space".</summary>
	public string ResetUrlBase { get; set; } = string.Empty;

	/// <summary>How long a password-reset token remains valid, in minutes.</summary>
	public int ResetTokenLifetimeMinutes { get; set; } = 30;
}
