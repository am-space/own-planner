using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Telegram;

namespace OwnPlanner.Web.Server.Services;

/// <summary>Rejects disabled or unauthenticated Telegram webhook requests before model binding reads JSON.</summary>
public sealed class TelegramWebhookSecretFilter(IOptions<TelegramOptions> options) : IAsyncResourceFilter
{
	private readonly TelegramOptions _options = options.Value;

	public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
	{
		var supplied = context.HttpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
		if (!_options.Enabled || !Matches(supplied, _options.WebhookSecret))
		{
			context.Result = new UnauthorizedResult();
			return;
		}

		await next();
	}

	internal static bool Matches(string? supplied, string expected)
	{
		if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected)) return false;
		var left = Encoding.UTF8.GetBytes(supplied);
		var right = Encoding.UTF8.GetBytes(expected);
		return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
	}
}
