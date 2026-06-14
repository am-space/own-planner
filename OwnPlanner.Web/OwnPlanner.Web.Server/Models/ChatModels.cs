namespace OwnPlanner.Web.Server.Models
{
	/// <summary>
	/// Request model for sending a chat message
	/// </summary>
	public class ChatRequest
	{
		/// <summary>
		/// The user's message/prompt
		/// </summary>
		public required string Message { get; set; }
	}

	/// <summary>
	/// Response model for chat messages
	/// </summary>
	public class ChatResponse
	{
		/// <summary>
		/// The AI's response message
		/// </summary>
		public required string Message { get; set; }

		/// <summary>
		/// Timestamp of the response
		/// </summary>
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;

		/// <summary>
		/// Session ID for tracking conversation
		/// </summary>
		public required string SessionId { get; set; }

		/// <summary>
		/// Current model prompt-token context length for this chat turn.
		/// </summary>
		public int? ContextLengthTokens { get; set; }

		/// <summary>
		/// Maximum allowed model prompt-token context length.
		/// </summary>
		public int MaxContextLengthTokens { get; set; }

		/// <summary>
		/// Remaining chat requests in the user's daily quota after this request.
		/// </summary>
		public int? RemainingDailyQuota { get; set; }

		/// <summary>
		/// When the daily quota resets (next UTC midnight).
		/// </summary>
		public DateTimeOffset? QuotaResetAtUtc { get; set; }
	}

	/// <summary>
	/// Request model for switching the planning mode
	/// </summary>
	public class SwitchModeRequest
	{
		/// <summary>
		/// The planning mode to switch to (GlobalPlanning, WeekPlanning, DayWork, Reflection, SystemAnalysis)
		/// </summary>
		public required string Mode { get; set; }
	}

	/// <summary>
	/// Response for starter prompts of a planning mode
	/// </summary>
	public class ModeStarterPromptsResponse
	{
		public required string Mode { get; set; }
		public required IReadOnlyList<string> StarterPrompts { get; set; }
	}

	/// <summary>
	/// Response for session status
	/// </summary>
	public class SessionStatusResponse
	{
		public required string SessionId { get; set; }
		public bool IsActive { get; set; }
		public int ActiveSessionsCount { get; set; }
       public string? CurrentMode { get; set; }
		public int? ContextLengthTokens { get; set; }
       public int MaxContextLengthTokens { get; set; }

		/// <summary>The user's effective daily request quota.</summary>
		public int? DailyQuotaLimit { get; set; }

		/// <summary>Requests the user has made so far today (UTC).</summary>
		public int? DailyQuotaUsed { get; set; }

		/// <summary>Remaining chat requests in the user's daily quota.</summary>
		public int? RemainingDailyQuota { get; set; }

		/// <summary>When the daily quota resets (next UTC midnight).</summary>
		public DateTimeOffset? QuotaResetAtUtc { get; set; }
	}
}
