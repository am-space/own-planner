using System.Text.Json;
using Mscc.GenerativeAI;
using Serilog;

namespace OwnPlanner.Infrastructure.Adapters
{
	/// <summary>
	/// Adapter for Gemini AI chat service with MCP tool integration
	/// </summary>
	public class ChatServiceAdapter : IAsyncDisposable
	{
		private readonly GoogleAI _googleAI;
		private readonly string _model;
		private readonly McpAdapter? _mcpClient;
		private readonly bool _shouldDisposeMcp;
		private readonly int _maxToolCallRounds;
		private Tools? _geminiTools;
		private GenerativeModel _generativeModel = null!; // Initialized in constructor
		private ChatSession _chat = null!; // Initialized in InitializeChatWithInstructions

		/// <summary>
		/// When this chat service was created
		/// </summary>
		public DateTime CreatedTime { get; }

		/// <summary>
		/// Last time GetResponse was called (tracks actual usage)
		/// </summary>
		public DateTime LastAccessTime { get; private set; }

		private void InitializeChatSession()
		{
			// Initialize the generative model and chat session with any available tools
			_generativeModel = _googleAI.GenerativeModel(_model, tools: _geminiTools);
			InitializeChatWithInstructions();
			Log.Information("Chat session initialized successfully");
		}

		public void ResetChatSession()
		{
			InitializeChatSession();
		}

		public ChatServiceAdapter(string apiKey, string model, int maxToolCallRounds = 10, McpAdapter? mcpAdapter = null)
		{
			Log.Debug("Creating ChatServiceAdapter with model: {Model}, MCP: {HasMcp}, MaxToolCallRounds: {MaxRounds}", model, mcpAdapter != null, maxToolCallRounds);
			_googleAI = new GoogleAI(apiKey);
			_model = model;
			_mcpClient = mcpAdapter;
			_shouldDisposeMcp = mcpAdapter != null; // Don't dispose injected adapter
			_maxToolCallRounds = maxToolCallRounds;
			// Initialize timestamps
			CreatedTime = DateTime.UtcNow;
			LastAccessTime = DateTime.UtcNow;

			if (_mcpClient != null)
			{
				Log.Information("Initializing MCP tools for Gemini...");
				// Only initialize MCP if not already initialized
				if (_geminiTools == null)
				{
					Task.Run(InitializeMcpAsync).Wait();
				}
			}

			InitializeChatSession();
			Log.Information("ChatServiceAdapter initialized successfully");
		}

		private void InitializeChatWithInstructions()
		{
			// Define the system instructions / initial prompt
			var systemInstructions =
				@"  You are a helpful personal planning assistant integrated into OwnPlanner application.

					Your capabilities:
					- Help users manage their tasks and to-do lists
					- Assist with note-taking and organization
					- Provide information about current date and time
					- Answer questions and provide helpful advice

					Available tools:
					- Task management: Create, list, update, and delete tasks
					- Note management: Create, list, update, and delete notes
					- Date/time information: Get current date and time
					- List tasks by focus date to see if the user has tasks planned for today or other dates

					Guidelines:
					- Be concise but friendly
					- When users ask to create tasks or notes, use the appropriate tools
					- Always confirm actions taken (e.g., ""I've created a task for..."")
					- If asked about the current date/time, use the datetime tool
					- Proactively suggest using tools when relevant
					- Format responses clearly and professionally, don't show entity IDs unless requested
					- Tools marked as read-only can be used without additional user confirmation

					Remember: You have access to real tools that can modify user data. Always use them when appropriate.";

			// Create initial history with system instructions
			var initialHistory = new List<ContentResponse>
			{
				new ContentResponse(systemInstructions),
				new ContentResponse("Understood! I'm ready to help you with your tasks, notes, and planning needs.","model")
			};

			// Start chat with the initial instructions
			_chat = _generativeModel.StartChat(history: initialHistory);

			Log.Debug("Chat initialized with system instructions");
		}

		private async Task InitializeMcpAsync()
		{
			if (_mcpClient == null) return;

			try
			{
				Log.Debug("Initializing MCP client...");
				await _mcpClient.InitializeAsync();
				
				var details = await _mcpClient.ListToolDetailsAsync();
				Log.Information("Retrieved {Count} MCP tool details", details.Count);

				if (details.Any())
				{
					var functionDeclarations = new List<FunctionDeclaration>();
					foreach (var d in details)
					{
						Schema? schema = null;
						try
						{
							// Build the Schema object from the JsonSchema property
							var jsonSchema = d.JsonSchema;
							if (jsonSchema.ValueKind != JsonValueKind.Undefined && jsonSchema.ValueKind != JsonValueKind.Null)
							{
								schema = ConvertJsonSchemaToGeminiSchema(jsonSchema);
							}
						}
						catch (Exception ex)
						{
							// Log schema parse issues but continue
						Log.Warning(ex, "Failed to parse schema for tool: {ToolName}", d.Name);
						}

						functionDeclarations.Add(new FunctionDeclaration
						{
							Name = d.Name,
							Description = d.Description,
							Parameters = schema
						});
						
						Log.Debug("Added function declaration: {ToolName}", d.Name);
					}

					_geminiTools = new Tools
					{
						new Tool { FunctionDeclarations = functionDeclarations }
					};

					Log.Information("[MCP] Loaded {ToolCount} tools for Gemini: {Tools}", functionDeclarations.Count, string.Join(", ", functionDeclarations.Select(f => f.Name)));
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "MCP initialization failed");
			}
		}

		private Schema? ConvertJsonSchemaToGeminiSchema(JsonElement jsonSchema)
		{
			try
			{
				// Use the built-in FromJsonElement method from the Schema class
				return Schema.FromJsonElement(jsonSchema);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Schema conversion failed");
				return null;
			}
		}

		private void LogUsageMetadata(GenerateContentResponse? response, string stage)
		{
			var usage = response?.UsageMetadata;
			if (usage == null)
			{
				Log.Debug("Gemini usage metadata not available at stage {Stage}", stage);
				return;
			}

			Log.Debug(
				"Gemini token usage ({Stage}): prompt={PromptTokens}, candidates={CandidateTokens}, total={TotalTokens}, cachedContent={CachedContentTokens}, toolUsePrompt={ToolUsePromptTokens}, thoughts={ThoughtsTokens}",
				stage,
				usage.PromptTokenCount,
				usage.CandidatesTokenCount,
				usage.TotalTokenCount,
				usage.CachedContentTokenCount,
				usage.ToolUsePromptTokenCount,
				usage.ThoughtsTokenCount);
		}

		public async Task<string> GetResponse(string text)
		{
			LastAccessTime = DateTime.UtcNow;

			Log.Debug("Getting response for prompt: {Prompt}", text);
			try
			{
				var response = await _chat.SendMessage(text);
				LogUsageMetadata(response, "user-message");
				int roundCount = 0;
				while (roundCount < _maxToolCallRounds)
				{
					var parts = response.Candidates?
						.FirstOrDefault()?
						.Content?
						.Parts;
					var functionCalls = parts?
						.Where(p => p.FunctionCall != null)
						.ToList();
					if (functionCalls == null || functionCalls.Count == 0)
					{
						Log.Debug("No function calls in response, exiting tool loop");
						break;
					}
					Log.Information("Processing {Count} function calls in round {Round}", functionCalls.Count, roundCount + 1);
					var toolResults = new List<Part>();
					foreach (var part in functionCalls)
					{
						var functionCall = part.FunctionCall;
						if (functionCall == null || _mcpClient == null)
						{
							continue;
						}
						Log.Information("[MCP] Gemini requested to call tool: {ToolName}", functionCall.Name);
						try
						{
							var argsDict = functionCall.Args != null
								? JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(functionCall.Args))
								: new Dictionary<string, object?>();
							var toolName = functionCall.Name;
							if (toolName.Contains(':'))
							{
								var nsSplit = toolName.Split(':', 2);
								toolName = nsSplit[1];
								Log.Debug("Stripped namespace prefix from tool name: {Original} -> {Stripped}", functionCall.Name, toolName);
							}
							var result = await _mcpClient.CallToolAsync(toolName, argsDict);
							Log.Debug("Tool {ToolName} executed successfully", toolName);
							toolResults.Add(new Part
							{
								FunctionResponse = new FunctionResponse
								{
									Name = functionCall.Name,
									Response = new Dictionary<string, object?>
									{
										{ "result", result }
									}
								}
							});
						}
						catch (Exception ex)
						{
							Log.Error(ex, "MCP tool execution failed: {ToolName}", functionCall.Name);

							toolResults.Add(new Part
							{
								FunctionResponse = new FunctionResponse
								{
									Name = functionCall.Name,
									Response = new Dictionary<string, object?>
									{
										{ "error", ex.Message }
									}
								}
							});
						}
					}
					if (toolResults.Count == 0)
					{
						Log.Warning("Tool round produced zero results; stopping further tool processing");
						break;
					}
					Log.Debug("Sending {Count} tool results back to model", toolResults.Count);
					response = await _chat.SendMessage(toolResults);
					LogUsageMetadata(response, $"tool-results-round-{roundCount + 1}");
					roundCount++;
				}
				if (roundCount >= _maxToolCallRounds)
				{
					Log.Warning("Reached maximum tool call rounds ({MaxRounds}). Returning current response.", _maxToolCallRounds);
				}
				string safeText;
				try
				{
					safeText = response.Text ?? string.Empty;
				}
				catch (Exception ex)
				{
					Log.Warning(ex, "Accessing response.Text failed; falling back to manual assembly");
					var textParts = response.Candidates?
						.FirstOrDefault()?
						.Content?
						.Parts?
						.Where(p => p.Text != null)
						.Select(p => p.Text)
						.ToList();
					if (textParts == null || textParts.Count == 0)
					{
						Log.Debug("No textual parts in response; returning empty string");
						return string.Empty;
					}
					safeText = string.Join(Environment.NewLine, textParts);
				}
				return safeText;
			}
			catch (Mscc.GenerativeAI.GeminiApiException ex)
			{
				if (ex.Message.Contains("required oneof field 'data' must have one initialized field"))
				{
					Log.Warning(ex, "GeminiApiException: Detected session corruption, resetting chat session and retrying...");
					ResetChatSession();
					return "I'm sorry, there was an issue processing your request. I've reset our conversation context. Could you please repeat your last message?";
				}
				throw;
			}
		}

		public async ValueTask DisposeAsync()
		{
			Log.Debug("Disposing ChatServiceAdapter...");
			
			if (_mcpClient != null && _shouldDisposeMcp)
			{
				await _mcpClient.DisposeAsync().ConfigureAwait(false);
			}
			
			Log.Information("ChatServiceAdapter disposed");
		}
	}
}
