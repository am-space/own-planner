namespace OwnPlanner.Application.Chat;

public sealed record ChatTurnResult(string Message, int? ContextLengthTokens);
