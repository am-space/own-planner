namespace OwnPlanner.Web.Server.Configuration
{
	/// <summary>
	/// Configuration settings for the AI chat service
	/// </summary>
	public class ChatSettings
	{
		public GeminiSettings Gemini { get; set; } = new();
		public McpSettings Mcp { get; set; } = new();
	}

	public class GeminiSettings
	{
		public string ApiKey { get; set; } = string.Empty;
		public string Model { get; set; } = "gemini-2.0-flash-exp";
		public int MaxToolCallRounds { get; set; } = 10;
	}

	public class McpSettings
	{
		public string Command { get; set; } = string.Empty;
		public string[] Arguments { get; set; } = [];
	}
}
