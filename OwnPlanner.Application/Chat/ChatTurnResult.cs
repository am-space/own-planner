namespace OwnPlanner.Application.Chat;

/// <summary>
/// The result of a single chat turn. <paramref name="InputTokens"/> and <paramref name="OutputTokens"/>
/// aggregate Gemini prompt/candidate token usage across every model call made while producing the turn
/// (the initial message plus any tool-result rounds), for backstop usage accounting.
/// </summary>
public sealed record ChatTurnResult(string Message, int? ContextLengthTokens, long InputTokens = 0, long OutputTokens = 0);
