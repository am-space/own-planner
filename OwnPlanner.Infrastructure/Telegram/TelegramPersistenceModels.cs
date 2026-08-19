using OwnPlanner.Application.Chat;

namespace OwnPlanner.Infrastructure.Telegram;

public sealed class TelegramConnectionToken
{
	public Guid Id { get; set; }
	public Guid UserId { get; set; }
	public string TokenHash { get; set; } = string.Empty;
	public DateTime ExpiresAtUtc { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? ConsumedAtUtc { get; set; }
}

public sealed class TelegramAccountLink
{
	public Guid Id { get; set; }
	public Guid UserId { get; set; }
	public long TelegramUserId { get; set; }
	public long ChatId { get; set; }
	public PlanningMode Mode { get; set; } = PlanningMode.DayWork;
	public DateTime ConnectedAtUtc { get; set; }
	public long? LastProcessedUpdateId { get; set; }
}

public sealed class TelegramProcessedUpdate
{
	public long UpdateId { get; set; }
	public DateTime ReservedAtUtc { get; set; }
	public DateTime? CompletedAtUtc { get; set; }
	public bool? Succeeded { get; set; }
}
