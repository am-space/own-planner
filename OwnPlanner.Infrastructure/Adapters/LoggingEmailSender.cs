using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Email;

namespace OwnPlanner.Infrastructure.Adapters;

/// <summary>
/// Development <see cref="IEmailSender"/> that logs the message instead of sending it,
/// so flows such as password reset can be exercised end-to-end without an email provider.
/// The full body is logged so links (e.g. the reset link) are visible.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
	public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
	{
		logger.LogInformation(
			"[DEV EMAIL] To: {Recipient} | Subject: {Subject}{NewLine}{Body}",
			to, subject, Environment.NewLine, htmlBody);

		return Task.CompletedTask;
	}
}
