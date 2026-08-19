using System.Text.Json.Serialization;

namespace OwnPlanner.Web.Server.Models;

public sealed class TelegramUpdate
{
	[JsonPropertyName("update_id")]
	public long UpdateId { get; set; }
	[JsonPropertyName("message")]
	public TelegramMessage? Message { get; set; }
}

public sealed class TelegramMessage
{
	[JsonPropertyName("message_id")]
	public long MessageId { get; set; }
	[JsonPropertyName("from")]
	public TelegramUser? From { get; set; }
	[JsonPropertyName("chat")]
	public TelegramChat? Chat { get; set; }
	[JsonPropertyName("text")]
	public string? Text { get; set; }
}

public sealed class TelegramUser
{
	[JsonPropertyName("id")]
	public long Id { get; set; }
	[JsonPropertyName("is_bot")]
	public bool IsBot { get; set; }
}

public sealed class TelegramChat
{
	[JsonPropertyName("id")]
	public long Id { get; set; }
	[JsonPropertyName("type")]
	public string Type { get; set; } = string.Empty;
}
