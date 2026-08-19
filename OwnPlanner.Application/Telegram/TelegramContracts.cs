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
	public int ProcessedUpdateRetentionDays { get; set; } = 7;
}

public sealed record TelegramConnectionStatus(
	bool Enabled,
	bool Connected,
	bool Pending,
	long? TelegramUserId,
	DateTimeOffset? ConnectedAtUtc,
	string? Mode);

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
	/// <summary>Returns the configured and persisted Telegram connection state for one OwnPlanner user.</summary>
	/// <param name="userId">The authenticated OwnPlanner user identifier.</param>
	/// <param name="cancellationToken">Cancels the database read.</param>
	Task<TelegramConnectionStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Invalidates earlier pending tokens and creates a new short-lived, single-use deep link.</summary>
	/// <param name="userId">The authenticated OwnPlanner user identifier.</param>
	/// <param name="cancellationToken">Cancels token persistence.</param>
	/// <returns>The Telegram deep link and its UTC expiry. Plaintext token material is returned only in the URL.</returns>
	Task<TelegramConnectionLink> CreateConnectionLinkAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Removes the user's Telegram mapping and all outstanding connection tokens without deleting planner data.</summary>
	/// <param name="userId">The authenticated OwnPlanner user identifier.</param>
	/// <param name="cancellationToken">Cancels the database operation.</param>
	Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Atomically consumes a plaintext connection token and establishes the one-to-one Telegram mapping.</summary>
	/// <param name="plaintextToken">The untrusted token received from Telegram's start parameter.</param>
	/// <param name="telegramUserId">The verified numeric Telegram sender identifier.</param>
	/// <param name="chatId">The verified numeric private-chat identifier.</param>
	/// <param name="cancellationToken">Cancels the linking transaction.</param>
	/// <returns>A stable result that does not disclose another account's identity.</returns>
	Task<TelegramLinkResult> ConsumeConnectionTokenAsync(string plaintextToken, long telegramUserId, long chatId, CancellationToken cancellationToken = default);

	/// <summary>Resolves an OwnPlanner identity only when both persisted Telegram identifiers match.</summary>
	/// <param name="telegramUserId">The verified numeric Telegram sender identifier.</param>
	/// <param name="chatId">The verified numeric private-chat identifier.</param>
	/// <param name="cancellationToken">Cancels the database read.</param>
	Task<TelegramLinkedAccount?> FindLinkedAccountAsync(long telegramUserId, long chatId, CancellationToken cancellationToken = default);

	/// <summary>Persists the selected Telegram planning mode for restoration after session expiry.</summary>
	/// <param name="userId">The mapped OwnPlanner user identifier.</param>
	/// <param name="mode">The selected planning mode.</param>
	/// <param name="cancellationToken">Cancels the database update.</param>
	Task SetModeAsync(Guid userId, PlanningMode mode, CancellationToken cancellationToken = default);

	/// <summary>Advances a link's update high-water mark when the update is newer, preventing stale turns from executing later.</summary>
	/// <param name="userId">The mapped OwnPlanner user identifier.</param>
	/// <param name="updateId">The Telegram update identifier.</param>
	/// <param name="cancellationToken">Cancels the atomic database update.</param>
	/// <returns><see langword="true"/> only when this update is newer than every claimed update for the link.</returns>
	Task<bool> TryAdvanceChatUpdateAsync(Guid userId, long updateId, CancellationToken cancellationToken = default);

	/// <summary>Reserves an update identifier exactly once before any bot or planner side effect.</summary>
	/// <param name="updateId">The globally unique Telegram update identifier.</param>
	/// <param name="cancellationToken">Cancels reservation persistence.</param>
	/// <returns><see cref="TelegramUpdateReservation.Duplicate"/> only for a previously reserved identifier.</returns>
	Task<TelegramUpdateReservation> ReserveUpdateAsync(long updateId, CancellationToken cancellationToken = default);

	/// <summary>Records the terminal processing outcome without making a failed update eligible for replay.</summary>
	/// <param name="updateId">The previously reserved Telegram update identifier.</param>
	/// <param name="succeeded">Whether processing completed successfully.</param>
	/// <param name="cancellationToken">Cancels the status update.</param>
	Task CompleteUpdateAsync(long updateId, bool succeeded, CancellationToken cancellationToken = default);
}

/// <summary>Sends plain-text messages through the configured Telegram bot.</summary>
public interface ITelegramBotClient
{
	/// <summary>Sends all text to a private chat, splitting it into Telegram-compatible messages without losing Unicode content.</summary>
	/// <param name="chatId">The verified numeric private-chat identifier.</param>
	/// <param name="text">Plain text to deliver.</param>
	/// <param name="cancellationToken">Cancels outbound delivery.</param>
	Task SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default);
}
