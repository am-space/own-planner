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
		private GenerativeModel _generativeModel;
		private ChatSession _chat;

		/// <summary>
		/// When this chat service was created
		/// </summary>
		public DateTime CreatedTime { get; }

		/// <summary>
		/// Last time GetResponse was called (tracks actual usage)
		/// </summary>
		public DateTime LastAccessTime { get; private set; }

		public ChatServiceAdapter(string apiKey, string model, int maxToolCallRounds = 10, McpAdapter? mcpAdapter = null)
		{
			Log.Debug("Creating ChatServiceAdapter with model: {Model}, MCP: {HasMcp}, MaxToolCallRounds: {MaxRounds}", model, mcpAdapter != null, maxToolCallRounds);
			
			_googleAI = new GoogleAI(apiKey);
			_model = model;
			_mcpClient = mcpAdapter;
			_shouldDisposeMcp = false; // Don't dispose injected adapter
			_maxToolCallRounds = maxToolCallRounds;

			// Initialize timestamps
			CreatedTime = DateTime.UtcNow;
			LastAccessTime = DateTime.UtcNow;

			if (_mcpClient != null)
			{
				Log.Information("Initializing MCP tools for Gemini...");
				// Initialize MCP connection and map available tools to Gemini function declarations
				Task.Run(InitializeMcpAsync).Wait();
			}

			// Initialize the generative model and chat session with any available tools
			_generativeModel = _googleAI.GenerativeModel(_model, tools: _geminiTools);
			_chat = _generativeModel.StartChat();
			
			Log.Information("ChatServiceAdapter initialized successfully");
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
							var schemaErrorMessage = $"Warning: Failed to parse schema for tool '{d.Name}': {ex.Message}";
							System.Console.WriteLine(schemaErrorMessage);
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

					var toolsMessage = $"[MCP] Loaded {functionDeclarations.Count} tools for Gemini: {string.Join(", ", functionDeclarations.Select(f => f.Name))}";
					System.Console.WriteLine(toolsMessage);
					System.Console.WriteLine();
					Log.Information(toolsMessage);
				}
			}
			catch (Exception ex)
			{
				var initErrorMessage = $"Warning: MCP initialization failed: {ex.Message}";
				System.Console.WriteLine(initErrorMessage);
				System.Console.WriteLine();
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
				System.Console.WriteLine($"Warning: Schema conversion failed: {ex.Message}");
				Log.Warning(ex, "Schema conversion failed");
				return null;
			}
		}

		public async Task<string> GetResponse(string text)
		{
			// Update last access time on every message
			LastAccessTime = DateTime.UtcNow;

			Log.Debug("Getting response for prompt: {Prompt}", text);
			var response = await _chat.SendMessage(text);
			
			// Loop to handle multiple rounds of tool calls
			int roundCount = 0;

			while (roundCount < _maxToolCallRounds)
			{
				var functionCalls = response.Candidates?.FirstOrDefault()?.Content.Parts
					.Where(p => p.FunctionCall != null)
					.ToList();

				if (functionCalls == null || !functionCalls.Any())
				{
					// No more tool calls, we have the final response
					Log.Debug("No function calls in response, returning final result");
					break;
				}

				Log.Information("Processing {Count} function calls in round {Round}", functionCalls.Count, roundCount + 1);

				// The model decided to call one or more tools
				var toolResults = new List<Part>();

				foreach (var part in functionCalls)
				{
					var functionCall = part.FunctionCall;
					if (functionCall == null || _mcpClient == null) continue;

					var toolCallMessage = $"[MCP] Gemini requested to call tool: {functionCall.Name}";
					System.Console.WriteLine(toolCallMessage);
					Log.Information(toolCallMessage);

					try
					{
						// Convert the arguments from object to Dictionary<string, object?>
						var argsDict = functionCall.Args != null 
							? JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(functionCall.Args)) 
							: new Dictionary<string, object?>();

						// Strip any namespace prefix (e.g., "default_api:") from the tool name
						// Gemini may add these prefixes, but MCP expects just the tool name
						var toolName = functionCall.Name;
						if (toolName.Contains(':'))
						{
							var parts = toolName.Split(':', 2);
							toolName = parts[1];
							Log.Debug("Stripped namespace prefix from tool name: {Original} -> {Stripped}", functionCall.Name, toolName);
						}

						// Call the tool via MCP
						var result = await _mcpClient.CallToolAsync(toolName, argsDict);
						Log.Debug("Tool {ToolName} executed successfully", toolName);
						
						toolResults.Add(new Part
						{
							FunctionResponse = new FunctionResponse
							{
								Name = functionCall.Name,
								Response = new { result }
							}
						});
					}
					catch (Exception ex)
					{
						var errorMessage = $"Warning: MCP tool '{functionCall.Name}' failed: {ex.Message}";
						System.Console.WriteLine(errorMessage);
						Log.Error(ex, "MCP tool execution failed: {ToolName}", functionCall.Name);
						
						// Inform the model that the tool call failed
						toolResults.Add(new Part
						{
							FunctionResponse = new FunctionResponse
							{
								Name = functionCall.Name,
								Response = new { error = ex.Message }
							}
						});
					}
				}

				// Send the tool results back to the model and continue the loop
				Log.Debug("Sending {Count} tool results back to model", toolResults.Count);
				response = await _chat.SendMessage(toolResults);
				roundCount++;
			}

			if (roundCount >= _maxToolCallRounds)
			{
				var warningMessage = $"Warning: Reached maximum tool call rounds ({_maxToolCallRounds}). Returning current response.";
				System.Console.WriteLine(warningMessage);
				Log.Warning(warningMessage);
			}

			return response.Text ?? string.Empty;
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
