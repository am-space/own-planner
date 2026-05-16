namespace OwnPlanner.Application.Chat;

public sealed class ChatContextLimitExceededException(int? currentContextLengthTokens, int maxContextLengthTokens)
    : InvalidOperationException($"Chat context limit of {maxContextLengthTokens:N0} tokens reached. Start a new chat or switch mode to reset the context.")
{
    public int? CurrentContextLengthTokens { get; } = currentContextLengthTokens;
    public int MaxContextLengthTokens { get; } = maxContextLengthTokens;
}
