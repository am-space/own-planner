using OwnPlanner.Application.Chat;

namespace OwnPlanner.Application.Telegram;

/// <summary>Configuration required to expose the optional Telegram presentation channel.</summary>
public sealed class TelegramOptions
{
	public bool Enabled { get; set; }
	public string BotToken { get; set; } = string.Empty;
	public string BotUsername { get; set; } = string.Empty;
	public string WebhookSecret { get; set; } = string.Empty;
	public int LinkTokenLifetimeMinutes { get; set; } = 15;
}

public sealed record TelegramConnectionStatus(
	bool Enabled,
	bool Connected,
	bool Pending,
	long? TelegramUserId,
	DateTimeOffset? ConnectedAtUtc,
	PlanningMode? Mode);

public sealed record TelegramConnectionLink(string Url, DateTimeOffset ExpiresAtUtc);

public enum TelegramLinkResult
{
	Linked,
	InvalidOrExpired,
	OwnPlannerAccountAlreadyLinked,
	TelegramAccountAlreadyLinked,
}

public sealed record TelegramLinkedAccount(Guid UserId, long TelegramUserId, long ChatId, PlanningMode Mode);

public enum TelegramUpdateReservation
{
	Reserved,
	Duplicate,
}

/// <summary>Owns Telegram account linking, identity lookup, mode persistence, and update deduplication.</summary>
public interface ITelegramIntegrationService
{
	Task<TelegramConnectionStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<TelegramConnectionLink> CreateConnectionLinkAsync(Guid userId, CancellationToken cancellationToken = default);
	Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<TelegramLinkResult> ConsumeConnectionTokenAsync(string plaintextToken, long telegramUserId, long chatId, CancellationToken cancellationToken = default);
	Task<TelegramLinkedAccount?> FindLinkedAccountAsync(long telegramUserId, long chatId, CancellationToken cancellationToken = default);
	Task SetModeAsync(Guid userId, PlanningMode mode, CancellationToken cancellationToken = default);
	Task<TelegramUpdateReservation> ReserveUpdateAsync(long updateId, CancellationToken cancellationToken = default);
	Task CompleteUpdateAsync(long updateId, bool succeeded, CancellationToken cancellationToken = default);
}

/// <summary>Sends plain-text messages through the configured Telegram bot.</summary>
public interface ITelegramBotClient
{
	Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default);
}
