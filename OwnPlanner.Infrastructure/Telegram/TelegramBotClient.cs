using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Telegram;

namespace OwnPlanner.Infrastructure.Telegram;

public sealed class TelegramBotClient(HttpClient httpClient, IOptions<TelegramOptions> options) : ITelegramBotClient, IDisposable
{
	private readonly TelegramOptions _options = options.Value;

	public async Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default)
	{
		foreach (var part in Split(text, 4096))
		{
			using var response = await httpClient.PostAsJsonAsync(
				$"bot{_options.BotToken}/sendMessage",
				new { chat_id = chatId, text = part },
				cancellationToken);
			response.EnsureSuccessStatusCode();
		}
	}

	internal static IReadOnlyList<string> Split(string text, int maxLength)
	{
		if (string.IsNullOrEmpty(text)) return [string.Empty];
		var parts = new List<string>();
		var start = 0;
		while (start < text.Length)
		{
			var length = Math.Min(maxLength, text.Length - start);
			if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
			var split = text.LastIndexOf('\n', start + length - 1, length);
			if (split >= start + maxLength / 2) length = split - start + 1;
			parts.Add(text.Substring(start, length));
			start += length;
		}
		return parts;
	}

	public void Dispose() => httpClient.Dispose();
}
