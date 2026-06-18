namespace OwnPlanner.Application.Email;

/// <summary>
/// Port for sending outbound email. Implementations live in the Infrastructure layer
/// (e.g. an SMTP adapter for production, a logging adapter for local development).
/// </summary>
public interface IEmailSender
{
	/// <summary>
	/// Sends an HTML email to a single recipient.
	/// </summary>
	/// <param name="to">Recipient email address.</param>
	/// <param name="subject">Message subject line.</param>
	/// <param name="htmlBody">HTML body of the message.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <exception cref="System.Exception">Thrown when the message cannot be delivered.</exception>
	Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
