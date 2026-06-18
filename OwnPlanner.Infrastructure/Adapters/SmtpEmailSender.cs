using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using OwnPlanner.Application.Email;

namespace OwnPlanner.Infrastructure.Adapters;

/// <summary>
/// <see cref="IEmailSender"/> adapter that delivers mail over SMTP using MailKit.
/// Provider-agnostic: switching transactional relays is a configuration change only.
/// </summary>
public class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
	public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
	{
		try
		{
			var message = new MimeMessage();
			message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
			message.To.Add(MailboxAddress.Parse(to));
			message.Subject = subject;
			message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

			using var client = new SmtpClient();
			await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, ct);

			if (!string.IsNullOrWhiteSpace(options.User))
			{
				await client.AuthenticateAsync(options.User, options.Password, ct);
			}

			await client.SendAsync(message, ct);
			await client.DisconnectAsync(true, ct);

			logger.LogInformation("Sent email to {Recipient} with subject {Subject}", to, subject);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send email to {Recipient} with subject {Subject}", to, subject);
			throw;
		}
	}
}
