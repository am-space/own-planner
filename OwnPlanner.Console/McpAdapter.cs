using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using Serilog;

namespace OwnPlanner.Console
{
	// Wrapper around the official MCP C# SDK client using Stdio transport
	public class McpAdapter : IAsyncDisposable
	{
		private readonly string _command;
		private readonly string[] _arguments;
		private McpClient? _client;
		private bool _disposed;

		public McpAdapter(string command, params string[] arguments)
		{
			// Accept a command and arguments to launch the MCP server process
			// Example: "dotnet", "run", "--project", "path/to/McpServer.csproj"
			// Or: "npx", "-y", "@modelcontextprotocol/server-everything"
			_command = command;
			_arguments = arguments ?? [];
			
			Log.Debug("McpAdapter created with command: {Command} {Arguments}", _command, string.Join(" ", _arguments));
		}

		private async Task EnsureClientAsync(CancellationToken cancellationToken)
		{
			if (_client != null) return;

			try
			{
				Log.Information("Creating MCP client transport...");
				
				var options = new StdioClientTransportOptions
				{
					Name = "OwnPlanner",
					Command = _command,
					Arguments = _arguments
				};
				
				var clientTransport = new StdioClientTransport(options);
				Log.Debug("StdioClientTransport created, initializing McpClient...");
				
				_client = await ModelContextProtocol.Client.McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken).ConfigureAwait(false);
				Log.Information("MCP client created successfully");
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Failed to create MCP stdio client. Command: {Command} {Arguments}", _command, string.Join(" ", _arguments));
				throw new InvalidOperationException($"Failed to create MCP stdio client. Command: {_command} {string.Join(" ", _arguments)}", ex);
			}
		}

		public async Task InitializeAsync(CancellationToken cancellationToken = default)
		{
			Log.Debug("Initializing MCP adapter...");
			await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

			// Log server info if available
			try
			{
				var name = _client!.ServerInfo?.Name ?? "unknown";
				var version = _client.ServerInfo?.Version ?? "";
				var message = $"[MCP] Connected to {name}{(string.IsNullOrEmpty(version) ? string.Empty : $" v{version}")}";
				System.Console.WriteLine(message);
				Log.Information(message);
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Failed to retrieve server info");
			}
		}

		public async Task<List<string>> ListToolsAsync(CancellationToken cancellationToken = default)
		{
			Log.Debug("Listing MCP tools...");
			await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

			var tools = await _client!.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
			var names = tools.Select(t => t.Name ?? string.Empty).Where(n => n.Length > 0).ToList();
			
			var message = $"[MCP] Found {names.Count} tools: {string.Join(", ", names)}";
			System.Console.WriteLine(message);
			Log.Information(message);
			
			return names;
		}

		public async Task<IList<McpClientTool>> ListToolDetailsAsync(CancellationToken cancellationToken = default)
		{
			Log.Debug("Listing MCP tool details...");
			await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

			return await _client!.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?>? arguments = null, CancellationToken cancellationToken = default)
		{
			Log.Debug("Calling MCP tool: {ToolName} with arguments: {Arguments}", toolName, JsonSerializer.Serialize(arguments));
			await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

			var result = await _client!.CallToolAsync(
				toolName,
				arguments ?? [],
				progress: null,
				serializerOptions: null,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			// Aggregate any text content into a single string
			var sbText = new StringBuilder();
			foreach (var c in result.Content)
			{
				if (string.Equals(c.Type, "text", StringComparison.OrdinalIgnoreCase))
				{
					// Try to access Text property on the concrete type if available
					var textProp = c.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
					if (textProp?.GetValue(c) is string text && !string.IsNullOrEmpty(text))
					{
						sbText.AppendLine(text);
					}
				}
			}
			var aggregated = sbText.ToString().Trim();
			if (!string.IsNullOrEmpty(aggregated))
			{
				Log.Debug("Tool {ToolName} returned text result", toolName);
				return aggregated;
			}

			// Fallback: serialize result if no text entries found
			var serialized = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false });
			Log.Debug("Tool {ToolName} returned serialized result", toolName);
			return serialized;
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed) return;
			_disposed = true;

			Log.Debug("Disposing MCP adapter...");
			
			if (_client != null)
			{
				await _client.DisposeAsync().ConfigureAwait(false);
				Log.Information("MCP adapter disposed");
			}
		}
	}
}
