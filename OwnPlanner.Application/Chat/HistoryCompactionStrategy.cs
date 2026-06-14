namespace OwnPlanner.Application.Chat;

/// <summary>How older conversation history is compacted when the context approaches its limit.</summary>
public enum HistoryCompactionStrategy
{
	/// <summary>Replace the older span with a model-generated summary. Falls back to <see cref="Trim"/> if summarization fails.</summary>
	Summarize,

	/// <summary>Drop the older span entirely, keeping only the system preamble and the most recent turns.</summary>
	Trim
}
