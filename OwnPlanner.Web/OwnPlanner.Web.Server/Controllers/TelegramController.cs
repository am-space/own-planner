using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.Usage;
using OwnPlanner.Web.Server.Models;
using OwnPlanner.Web.Server.Services;

namespace OwnPlanner.Web.Server.Controllers;

[ApiController]
[Route("api/telegram")]
public sealed class TelegramController(
	ITelegramIntegrationService integrationService,
	ITelegramBotClient botClient,
	IChatSessionManager sessionManager,
	IUsageQuotaService usageQuotaService,
	TelegramChatLock chatLock,
	IOptions<TelegramOptions> options,
	ILogger<TelegramController> logger) : ControllerBase
{
	private readonly TelegramOptions _options = options.Value;

	[Authorize]
	[HttpGet("connection")]
	public async Task<IActionResult> GetConnection(CancellationToken cancellationToken)
		=> Ok(await integrationService.GetStatusAsync(GetCurrentUserId(), cancellationToken));

	[Authorize]
	[HttpPost("connection")]
	public async Task<IActionResult> CreateConnection(CancellationToken cancellationToken)
	{
		if (!_options.Enabled) return Conflict(new { message = "Telegram integration is disabled." });
		return Ok(await integrationService.CreateConnectionLinkAsync(GetCurrentUserId(), cancellationToken));
	}

	[Authorize]
	[HttpDelete("connection")]
	public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
	{
		var userId = GetCurrentUserId();
		var status = await integrationService.GetStatusAsync(userId, cancellationToken);
		if (status.TelegramUserId is long telegramUserId)
		{
			await sessionManager.RemoveSessionAsync(SessionId(telegramUserId));
		}
		await integrationService.DisconnectAsync(userId, cancellationToken);
		return NoContent();
	}

	[AllowAnonymous]
	[HttpPost("webhook")]
	[ServiceFilter(typeof(TelegramWebhookSecretFilter))]
	public async Task<IActionResult> Webhook(
		[FromBody] TelegramUpdate update,
		CancellationToken cancellationToken)
	{
		if (update.UpdateId <= 0) return BadRequest();
		if (await integrationService.ReserveUpdateAsync(update.UpdateId, cancellationToken) == TelegramUpdateReservation.Duplicate) return Ok();

		var message = update.Message;
		if (message?.From is null || message.Chat is null || message.From.IsBot ||
			!string.Equals(message.Chat.Type, "private", StringComparison.Ordinal) || string.IsNullOrEmpty(message.Text))
		{
			await integrationService.CompleteUpdateAsync(update.UpdateId, true, cancellationToken);
			return Ok();
		}

		try
		{
			using (await chatLock.AcquireAsync(message.Chat.Id, cancellationToken))
			{
				await ProcessMessageAsync(update.UpdateId, message.From.Id, message.Chat.Id, message.Text, cancellationToken);
			}
			await integrationService.CompleteUpdateAsync(update.UpdateId, true, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Telegram update {UpdateId} failed", update.UpdateId);
			await integrationService.CompleteUpdateAsync(update.UpdateId, false, cancellationToken);
			try { await botClient.SendTextAsync(message.Chat.Id, "Sorry, I couldn't process that message. Please try a new message later.", cancellationToken); }
			catch (Exception sendEx) { logger.LogWarning(sendEx, "Could not send Telegram failure response for update {UpdateId}", update.UpdateId); }
		}

		// A reserved update is always acknowledged. Telegram retries must never replay a possibly partial planning turn.
		return Ok();
	}

	private async Task ProcessMessageAsync(long updateId, long telegramUserId, long chatId, string text, CancellationToken cancellationToken)
	{
		var command = text.Trim();
		if (command.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
		{
			var token = command[7..].Trim().Split(' ', 2)[0];
			var result = await integrationService.ConsumeConnectionTokenAsync(token, telegramUserId, chatId, cancellationToken);
			await botClient.SendTextAsync(chatId, result == TelegramLinkResult.Linked
				? "Telegram is connected to OwnPlanner. You're in Day mode. Send a planning message or /help."
				: "That connection link is invalid, expired, or cannot be used. Create a new link in OwnPlanner Settings.", cancellationToken);
			return;
		}

		var account = await integrationService.FindLinkedAccountAsync(telegramUserId, chatId, cancellationToken);
		if (account is null)
		{
			await botClient.SendTextAsync(chatId, "Connect Telegram from OwnPlanner Settings before using this bot.", cancellationToken);
			return;
		}
		if (!await integrationService.TryAdvanceChatUpdateAsync(account.UserId, updateId, cancellationToken)) return;

		var sessionId = SessionId(telegramUserId);
		if (command.StartsWith('/'))
		{
			await ProcessCommandAsync(account, sessionId, command, cancellationToken);
			return;
		}

		try
		{
			await usageQuotaService.CheckAndReserveAsync(account.UserId.ToString(), cancellationToken);
			var chat = await sessionManager.GetOrCreateSessionAsync(sessionId, account.UserId.ToString(), cancellationToken);
			if (chat.CurrentMode != account.Mode) await chat.SwitchModeAsync(account.Mode, cancellationToken);
			var response = await chat.GetResponseAsync(text, cancellationToken);
			await RecordTokensAsync(account.UserId, response, cancellationToken);
			await botClient.SendTextAsync(chatId, response.Message, cancellationToken);
		}
		catch (UsageQuotaExceededException ex)
		{
			await botClient.SendTextAsync(chatId, $"Usage limit reached. Try again after {ex.ResetAtUtc:u}.", cancellationToken);
		}
		catch (ChatContextLimitExceededException)
		{
			await botClient.SendTextAsync(chatId, "This conversation is too long. Use /new and try again.", cancellationToken);
		}
	}

	private async Task ProcessCommandAsync(TelegramLinkedAccount account, string sessionId, string command, CancellationToken cancellationToken)
	{
		var verb = command.Split(' ', 2)[0].Split('@', 2)[0].ToLowerInvariant();
		switch (verb)
		{
			case "/start":
			case "/help":
				await botClient.SendTextAsync(account.ChatId, "Commands: /mode <day|week|global|reflection|analysis>, /new, /status, /unlink. Send ordinary text to plan.", cancellationToken);
				break;
			case "/new":
				await sessionManager.RemoveSessionAsync(sessionId);
				await botClient.SendTextAsync(account.ChatId, "Started a new Telegram conversation.", cancellationToken);
				break;
			case "/status":
				var usage = await usageQuotaService.GetStatusAsync(account.UserId.ToString(), cancellationToken);
				await botClient.SendTextAsync(account.ChatId, $"Mode: {ModeName(account.Mode)}. Remaining daily messages: {(usage.Remaining?.ToString() ?? "unlimited")}.", cancellationToken);
				break;
			case "/unlink":
				await sessionManager.RemoveSessionAsync(sessionId);
				await integrationService.DisconnectAsync(account.UserId, cancellationToken);
				await botClient.SendTextAsync(account.ChatId, "Telegram has been disconnected. Your OwnPlanner data was not deleted.", cancellationToken);
				break;
			case "/mode":
				var argument = command.Split(' ', 2).ElementAtOrDefault(1)?.Trim();
				if (!TryParseMode(argument, out var mode))
				{
					await botClient.SendTextAsync(account.ChatId, "Use /mode day, week, global, reflection, or analysis.", cancellationToken);
					break;
				}
				var chat = await sessionManager.GetOrCreateSessionAsync(sessionId, account.UserId.ToString(), cancellationToken);
				await chat.SwitchModeAsync(mode, cancellationToken);
				await integrationService.SetModeAsync(account.UserId, mode, cancellationToken);
				await botClient.SendTextAsync(account.ChatId, $"Switched to {ModeName(mode)} mode.", cancellationToken);
				break;
			default:
				await botClient.SendTextAsync(account.ChatId, "Unknown command. Use /help.", cancellationToken);
				break;
		}
	}

	private async Task RecordTokensAsync(Guid userId, ChatTurnResult response, CancellationToken cancellationToken)
	{
		try { await usageQuotaService.RecordTokensAsync(userId.ToString(), response.InputTokens, response.OutputTokens, cancellationToken); }
		catch (Exception ex) { logger.LogWarning(ex, "Failed to record Telegram chat token usage for user {UserId}", userId); }
	}

	private Guid GetCurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
	private static string SessionId(long telegramUserId) => $"telegram:{telegramUserId}";
	private static bool TryParseMode(string? value, out PlanningMode mode)
	{
		mode = value?.ToLowerInvariant() switch
		{
			"day" => PlanningMode.DayWork, "week" => PlanningMode.WeekPlanning,
			"global" => PlanningMode.GlobalPlanning, "reflection" => PlanningMode.Reflection,
			"analysis" => PlanningMode.SystemAnalysis, _ => (PlanningMode)(-1),
		};
		return Enum.IsDefined(mode);
	}
	private static string ModeName(PlanningMode mode) => mode switch
	{
		PlanningMode.DayWork => "Day", PlanningMode.WeekPlanning => "Week", PlanningMode.GlobalPlanning => "Global",
		PlanningMode.Reflection => "Reflection", PlanningMode.SystemAnalysis => "Analysis", _ => mode.ToString(),
	};
}
