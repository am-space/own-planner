using System.Text.Json;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using OwnPlanner.Application.Chat;
using Serilog;
using ChatMessage = OwnPlanner.Application.Chat.ChatMessage;

namespace OwnPlanner.Infrastructure.Adapters
{
	/// <summary>
	/// Adapter for Gemini AI chat service with MCP tool integration
	/// </summary>
	public class ChatServiceAdapter : IChatAdapter
	{
		private const string SearchAgentToolName = "search_agent_call";
		private const string SearchAgentToolSchema = """
		{
		  "type": "object",
		  "properties": {
		    "query": {
		      "type": "string",
		      "description": "The exact web search query to execute."
		    }
		  },
		  "required": ["query"]
		}
		""";
		private static readonly FunctionDeclaration SearchAgentFunctionDeclaration = BuildSearchAgentFunctionDeclaration();

		private readonly GoogleAI _googleAi;
		private readonly string _model;
		private readonly IMcpAdapter? _mcpClient;
		private readonly bool _shouldDisposeMcp;
		private readonly int _maxToolCallRounds;
		private Tools? _geminiTools;
		private List<FunctionDeclaration> _allFunctionDeclarations = [];
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

		public int? CurrentContextLengthTokens { get; private set; }

		private void InitializeChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null, IReadOnlyList<ChatMessage>? history = null)
		{
			CurrentContextLengthTokens = null;
			// Rebuild tool set, applying allow-list filter when provided
			var declarations = GetFunctionDeclarations(allowedTools);
			if (declarations.Count > 0)
			{
				_geminiTools = new Tools { new Tool { FunctionDeclarations = declarations } };
			}
			else
			{
				_geminiTools = null;
			}

			_generativeModel = _geminiTools != null
				? _googleAi.GenerativeModel(_model, tools: _geminiTools)
				: _googleAi.GenerativeModel(_model);
			InitializeChatWithInstructions(systemPrompt, history);
			Log.Information("Chat session initialized successfully");
		}

		public void ResetChatSession(string? systemPrompt = null, IReadOnlyList<string>? allowedTools = null)
		{
			InitializeChatSession(systemPrompt, allowedTools);
		}

		public void RebuildSession(string systemPrompt, IReadOnlyList<string>? allowedTools, IReadOnlyList<ChatMessage> history)
		{
			Log.Information("Rebuilding chat session with {Count} replayed history messages", history.Count);
			InitializeChatSession(systemPrompt, allowedTools, history);
		}

		public ChatServiceAdapter(string apiKey, string model, int maxToolCallRounds = 10, IMcpAdapter? mcpAdapter = null)
		{
			Log.Debug("Creating ChatServiceAdapter with model: {Model}, MCP: {HasMcp}, MaxToolCallRounds: {MaxRounds}", model, mcpAdapter != null, maxToolCallRounds);
			_googleAi = new GoogleAI(apiKey);
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

		private void InitializeChatWithInstructions(string? systemPrompt = null, IReadOnlyList<ChatMessage>? history = null)
		{
			// Define the system instructions / initial prompt
			var systemInstructions = systemPrompt ??
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

			// Replay any prior conversation (used when rebuilding the session after history compaction)
			if (history != null)
			{
				foreach (var message in history)
				{
					var role = message.Role == ChatRole.Model ? "model" : "user";
					initialHistory.Add(new ContentResponse(message.Text, role));
				}
			}

			// Start chat with the initial instructions
			_chat = _generativeModel.StartChat(history: initialHistory);

			Log.Debug("Chat initialized with system instructions and {Count} replayed messages", history?.Count ?? 0);
		}

		private async Task InitializeMcpAsync()
		{
			if (_mcpClient == null) return;

			try
			{
				Log.Debug("Initializing MCP client...");
				await _mcpClient.InitializeAsync();
				
				var details = await _mcpClient.ListToolDetailsAsync().ConfigureAwait(false);
				Log.Information("Retrieved {Count} MCP tool details", details.Count);

				if (details.Any())
				{
					_allFunctionDeclarations = [];
					foreach (var d in details)
					{
						Schema? schema = null;
						try
						{
							// Build the Schema object from the JsonSchema property
							if (d.JsonSchema is JsonElement jsonSchema &&
								jsonSchema.ValueKind != JsonValueKind.Undefined &&
								jsonSchema.ValueKind != JsonValueKind.Null)
							{
								schema = ConvertJsonSchemaToGeminiSchema(jsonSchema);
							}
						}
						catch (Exception ex)
						{
							// Log schema parse issues but continue
						Log.Warning(ex, "Failed to parse schema for tool: {ToolName}", d.Name);
						}

						_allFunctionDeclarations.Add(new FunctionDeclaration
						{
							Name = d.Name,
							Description = d.Description,
							Parameters = schema
						});

						Log.Debug("Added function declaration: {ToolName}", d.Name);
					}

					Log.Information("[MCP] Loaded {ToolCount} tools for Gemini: {Tools}", _allFunctionDeclarations.Count, string.Join(", ", _allFunctionDeclarations.Select(f => f.Name)));
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "MCP initialization failed");
			}
		}

		private List<FunctionDeclaration> GetFunctionDeclarations(IReadOnlyList<string>? allowedTools = null)
		{
			var declarations = new List<FunctionDeclaration>(_allFunctionDeclarations.Count + 1)
			{
				SearchAgentFunctionDeclaration
			};

			declarations.AddRange(_allFunctionDeclarations);

			if (allowedTools?.Count > 0)
			{
				declarations = declarations
					.Where(f => f.Name != null && allowedTools.Contains(f.Name))
					.ToList();
			}

			Log.Debug("Configured {ToolCount} Gemini tools: {Tools}", declarations.Count, string.Join(", ", declarations.Select(f => f.Name)));
			return declarations;
		}

		private static FunctionDeclaration BuildSearchAgentFunctionDeclaration()
		{
			using var searchSchemaDocument = JsonDocument.Parse(SearchAgentToolSchema);
			return new FunctionDeclaration
			{
				Name = SearchAgentToolName,
				Description = "Search the web for current factual information and return a concise summary.",
				Parameters = ConvertJsonSchemaToGeminiSchema(searchSchemaDocument.RootElement.Clone())
			};
		}

		private static Schema? ConvertJsonSchemaToGeminiSchema(JsonElement jsonSchema)
		{
			try
			{
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

		private void UpdateCurrentContextLength(GenerateContentResponse? response)
		{
			CurrentContextLengthTokens = response?.UsageMetadata?.PromptTokenCount;
		}

		private async Task<string> ExecuteSearchAgentCallAsync(IReadOnlyDictionary<string, object?>? arguments)
		{
			var query = ToolArgumentParser.GetStringArgument(arguments, "query");
			if (string.IsNullOrWhiteSpace(query))
			{
				throw new InvalidOperationException($"Tool '{SearchAgentToolName}' requires a non-empty 'query' argument.");
			}

			Log.Information("Executing local search agent call");
			Log.Debug("Query: {Query}", query);

			var searchModel = _googleAi.GenerativeModel(_model);
			searchModel.UseGoogleSearch = true;

			var searchChat = searchModel.StartChat(history:
			[
				new ContentResponse("You are a focused web search assistant. Use Google Search to answer the provided query with a concise factual summary. Do not call tools. Prefer current information when available."),
				new ContentResponse("Understood. I will search and return a concise factual summary.", "model")
			]);

			var response = await searchChat.SendMessage(query).ConfigureAwait(false);
			LogUsageMetadata(response, "search-agent-call");
			return GetSafeResponseText(response);
		}

		public async Task<string> SummarizeAsync(string conversationText, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(conversationText))
			{
				return string.Empty;
			}

			Log.Information("Summarizing earlier conversation for history compaction");

			// Use a separate, tool-less session so summarization never touches the active chat history.
			var summaryModel = _googleAi.GenerativeModel(_model);
			var summaryChat = summaryModel.StartChat(history:
			[
				new ContentResponse("You compress the earlier part of a personal-planning conversation into a brief factual summary. Preserve concrete outcomes: decisions made, tasks/notes/goals created or changed, and any open threads or pending user requests. Use a few short bullet points. Do not invent details and do not add commentary."),
				new ContentResponse("Understood. I will return a concise factual summary.", "model")
			]);

			var response = await summaryChat.SendMessage(conversationText).ConfigureAwait(false);
			LogUsageMetadata(response, "history-summary");
			return GetSafeResponseText(response);
		}

		private async Task<string> ExecuteToolCallAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments)
		{
			if (toolName == SearchAgentToolName)
			{
				return await ExecuteSearchAgentCallAsync(arguments).ConfigureAwait(false);
			}

			if (_mcpClient == null)
			{
				throw new InvalidOperationException($"Tool '{toolName}' is unavailable because MCP is not configured.");
			}

			return await _mcpClient.CallToolAsync(toolName, arguments).ConfigureAwait(false);
		}

		private string GetSafeResponseText(GenerateContentResponse response)
		{
			try
			{
				return response.Text ?? string.Empty;
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

				return string.Join(Environment.NewLine, textParts);
			}
		}

		public async Task<ChatTurnResult> GetResponse(string text)
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
						if (functionCall == null)
						{
							continue;
						}
						Log.Information("Gemini requested to call tool: {ToolName}", functionCall.Name);
						try
						{
							var argsDict = functionCall.Args != null
								? JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(functionCall.Args))
								: new Dictionary<string, object?>();
							var toolName = functionCall.Name;
							if (string.IsNullOrEmpty(toolName))
							{
								Log.Warning("Function call has null or empty name, skipping");
								continue;
							}

							if (toolName.Contains(':'))
							{
								var nsSplit = toolName.Split(':', 2);
								toolName = nsSplit[1];
								Log.Debug("Stripped namespace prefix from tool name: {Original} -> {Stripped}", functionCall.Name, toolName);
							}
							var result = await ExecuteToolCallAsync(toolName, argsDict).ConfigureAwait(false);
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

				UpdateCurrentContextLength(response);
				return new ChatTurnResult(GetSafeResponseText(response), CurrentContextLengthTokens);
			}
			catch (GeminiApiException ex)
			{
				if (ex.Message.Contains("required oneof field 'data' must have one initialized field"))
				{
					Log.Warning(ex, "GeminiApiException: Detected session corruption, resetting chat session and retrying...");
					ResetChatSession();
					return new ChatTurnResult("I'm sorry, there was an issue processing your request. I've reset our conversation context. Could you please repeat your last message?", null);
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
