using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Telegram;

public sealed class TelegramIntegrationService(
	AuthDbContext db,
	IOptions<TelegramOptions> options,
	TimeProvider timeProvider) : ITelegramIntegrationService
{
	private readonly TelegramOptions _options = options.Value;

	public async Task<TelegramConnectionStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var link = await db.TelegramAccountLinks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
		var now = timeProvider.GetUtcNow().UtcDateTime;
		var pending = link is null && await db.TelegramConnectionTokens.AsNoTracking()
			.AnyAsync(x => x.UserId == userId && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken);

		return new TelegramConnectionStatus(
			_options.Enabled,
			link is not null,
			pending,
			link?.TelegramUserId,
			link is null ? null : new DateTimeOffset(link.ConnectedAtUtc, TimeSpan.Zero),
			link?.Mode.ToString());
	}

	public async Task<TelegramConnectionLink> CreateConnectionLinkAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		EnsureEnabled();
		if (await db.TelegramAccountLinks.AnyAsync(x => x.UserId == userId, cancellationToken))
		{
			throw new InvalidOperationException("Disconnect the existing Telegram account before creating another link.");
		}

		var now = timeProvider.GetUtcNow().UtcDateTime;
		await db.TelegramConnectionTokens.Where(x => x.UserId == userId && x.ConsumedAtUtc == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAtUtc, now), cancellationToken);

		var plaintext = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
		var expiresAt = now.AddMinutes(Math.Clamp(_options.LinkTokenLifetimeMinutes, 1, 60));
		db.TelegramConnectionTokens.Add(new TelegramConnectionToken
		{
			Id = Guid.NewGuid(),
			UserId = userId,
			TokenHash = HashToken(plaintext),
			CreatedAtUtc = now,
			ExpiresAtUtc = expiresAt,
		});
		await db.SaveChangesAsync(cancellationToken);

		return new TelegramConnectionLink($"https://t.me/{_options.BotUsername.TrimStart('@')}?start={plaintext}", new DateTimeOffset(expiresAt, TimeSpan.Zero));
	}

	public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		await db.TelegramAccountLinks.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
		await db.TelegramConnectionTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<TelegramLinkResult> ConsumeConnectionTokenAsync(string plaintextToken, long telegramUserId, long chatId, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(plaintextToken) || telegramUserId <= 0 || chatId <= 0)
		{
			return TelegramLinkResult.InvalidOrExpired;
		}

		var now = timeProvider.GetUtcNow().UtcDateTime;
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		var token = await db.TelegramConnectionTokens.SingleOrDefaultAsync(
			x => x.TokenHash == HashToken(plaintextToken) && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now,
			cancellationToken);
		if (token is null)
		{
			return TelegramLinkResult.InvalidOrExpired;
		}

		if (await db.TelegramAccountLinks.AnyAsync(x => x.UserId == token.UserId, cancellationToken))
		{
			return TelegramLinkResult.OwnPlannerAccountAlreadyLinked;
		}

		if (await db.TelegramAccountLinks.AnyAsync(x => x.TelegramUserId == telegramUserId || x.ChatId == chatId, cancellationToken))
		{
			return TelegramLinkResult.TelegramAccountAlreadyLinked;
		}

		token.ConsumedAtUtc = now;
		db.TelegramAccountLinks.Add(new TelegramAccountLink
		{
			Id = Guid.NewGuid(), UserId = token.UserId, TelegramUserId = telegramUserId, ChatId = chatId,
			Mode = PlanningMode.DayWork, ConnectedAtUtc = now,
		});
		await db.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return TelegramLinkResult.Linked;
	}

	public async Task<TelegramLinkedAccount?> FindLinkedAccountAsync(long telegramUserId, long chatId, CancellationToken cancellationToken = default)
	{
		var link = await db.TelegramAccountLinks.AsNoTracking()
			.SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId && x.ChatId == chatId, cancellationToken);
		return link is null ? null : new TelegramLinkedAccount(link.UserId, link.TelegramUserId, link.ChatId, link.Mode);
	}

	public async Task SetModeAsync(Guid userId, PlanningMode mode, CancellationToken cancellationToken = default)
	{
		await db.TelegramAccountLinks.Where(x => x.UserId == userId)
			.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Mode, mode), cancellationToken);
	}

	public async Task<bool> TryAdvanceChatUpdateAsync(Guid userId, long updateId, CancellationToken cancellationToken = default)
	{
		var updated = await db.TelegramAccountLinks
			.Where(x => x.UserId == userId && (x.LastProcessedUpdateId == null || x.LastProcessedUpdateId < updateId))
			.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastProcessedUpdateId, updateId), cancellationToken);
		return updated == 1;
	}

	public async Task<TelegramUpdateReservation> ReserveUpdateAsync(long updateId, CancellationToken cancellationToken = default)
	{
		db.ChangeTracker.Clear();
		var now = timeProvider.GetUtcNow().UtcDateTime;
		var retentionDays = Math.Clamp(_options.ProcessedUpdateRetentionDays, 1, 30);
		var retentionCutoff = now.AddDays(-retentionDays);
		await db.TelegramProcessedUpdates.Where(x => x.ReservedAtUtc < retentionCutoff).ExecuteDeleteAsync(cancellationToken);
		if (await db.TelegramProcessedUpdates.AsNoTracking().AnyAsync(x => x.UpdateId == updateId, cancellationToken))
		{
			return TelegramUpdateReservation.Duplicate;
		}
		db.TelegramProcessedUpdates.Add(new TelegramProcessedUpdate { UpdateId = updateId, ReservedAtUtc = now });
		try
		{
			await db.SaveChangesAsync(cancellationToken);
			return TelegramUpdateReservation.Reserved;
		}
		catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
		{
			db.ChangeTracker.Clear();
			return TelegramUpdateReservation.Duplicate;
		}
	}

	public Task CompleteUpdateAsync(long updateId, bool succeeded, CancellationToken cancellationToken = default)
		=> db.TelegramProcessedUpdates.Where(x => x.UpdateId == updateId).ExecuteUpdateAsync(
			setters => setters.SetProperty(x => x.CompletedAtUtc, timeProvider.GetUtcNow().UtcDateTime).SetProperty(x => x.Succeeded, succeeded),
			cancellationToken);

	private void EnsureEnabled()
	{
		if (!_options.Enabled) throw new InvalidOperationException("Telegram integration is disabled.");
	}

	private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	internal static bool IsUniqueConstraintViolation(DbUpdateException exception)
		=> exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
