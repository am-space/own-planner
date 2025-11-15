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
	}

	/// <summary>
	/// Response for session status
	/// </summary>
	public class SessionStatusResponse
	{
		public required string SessionId { get; set; }
		public bool IsActive { get; set; }
		public int ActiveSessionsCount { get; set; }
	}
}
