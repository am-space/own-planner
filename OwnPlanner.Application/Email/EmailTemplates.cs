namespace OwnPlanner.Application.Email;

/// <summary>
/// Builds the HTML bodies for transactional emails sent by the application.
/// </summary>
public static class EmailTemplates
{
	/// <summary>
	/// Builds the password-reset email containing the reset link and its expiry.
	/// </summary>
	public static (string Subject, string HtmlBody) PasswordReset(string resetLink, int lifetimeMinutes)
	{
		const string subject = "Reset your OwnPlanner password";

		var htmlBody = $"""
			<div style="font-family: Arial, sans-serif; font-size: 14px; color: #1a1a1a;">
				<h2 style="margin: 0 0 16px;">Reset your password</h2>
				<p>We received a request to reset the password for your OwnPlanner account.</p>
				<p>
					<a href="{resetLink}"
					   style="display: inline-block; padding: 10px 18px; background: #1976d2; color: #ffffff; text-decoration: none; border-radius: 4px;">
						Reset password
					</a>
				</p>
				<p>Or copy and paste this link into your browser:</p>
				<p><a href="{resetLink}">{resetLink}</a></p>
				<p style="color: #666;">This link expires in {lifetimeMinutes} minutes and can be used once.</p>
				<p style="color: #666;">If you didn't request a password reset, you can safely ignore this email.</p>
			</div>
			""";

		return (subject, htmlBody);
	}
}
