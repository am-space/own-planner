using Microsoft.Extensions.Configuration;
using Serilog;
using Spectre.Console;

namespace OwnPlanner.Console
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			// Configure Serilog for console application logging (file only, to not interfere with chat UI)
			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.WriteTo.File("logs/console-.log", rollingInterval: Serilog.RollingInterval.Day)
				.CreateLogger();

			try
			{
				Log.Information("Starting OwnPlanner Console Chat");
				
				// Log application paths for debugging
				var currentDirectory = Directory.GetCurrentDirectory();
				var baseDirectory = AppContext.BaseDirectory;
				Log.Information("Current Directory: {CurrentDirectory}", currentDirectory);
				Log.Information("Base Directory: {BaseDirectory}", baseDirectory);

				// Load configuration from appsettings.json
				var configuration = new ConfigurationBuilder()
					.SetBasePath(Directory.GetCurrentDirectory())
					.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
					.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
					.Build();

				var settings = configuration.Get<AppSettings>() ?? new AppSettings();

				// Validate API key
				if (string.IsNullOrWhiteSpace(settings.Gemini.ApiKey))
				{
					AnsiConsole.MarkupLine("[red]Error: Gemini API key is not configured.[/]");
					AnsiConsole.MarkupLine("[yellow]Please set your API key in appsettings.Development.json[/]");
					AnsiConsole.MarkupLine("[dim]You can get an API key from: https://makersuite.google.com/app/apikey[/]");
					return;
				}

				AnsiConsole.MarkupLine("[bold cyan]Console Chat with Gemini LLM[/]");
				AnsiConsole.MarkupLine($"[dim]Model: {settings.Gemini.Model}[/]");
				
				// Create MCP adapter if configured
				McpAdapter? mcpAdapter = null;
				if (!string.IsNullOrEmpty(settings.Mcp.Command))
				{
					AnsiConsole.MarkupLine($"[dim]MCP enabled: {settings.Mcp.Command} {string.Join(" ", settings.Mcp.Arguments)}[/]");
					Log.Information("Creating MCP adapter: {Command} {Arguments}", settings.Mcp.Command, string.Join(" ", settings.Mcp.Arguments));
					
					try
					{
						mcpAdapter = new McpAdapter(settings.Mcp.Command, settings.Mcp.Arguments);
						Log.Information("MCP adapter created successfully");
					}
					catch (Exception ex)
					{
						Log.Error(ex, "Failed to create MCP adapter");
						AnsiConsole.MarkupLine($"[yellow]Warning: Failed to create MCP adapter: {ex.Message}[/]");
					}
				}
				
				AnsiConsole.MarkupLine("[dim]Type 'exit' to end the chat.[/]");
				AnsiConsole.WriteLine();

				await using (mcpAdapter)
				{
					await using var chatService = new ChatService(
						settings.Gemini.ApiKey, 
						settings.Gemini.Model,
						settings.Gemini.MaxToolCallRounds,
						mcpAdapter);

					while (true)
					{
						AnsiConsole.Markup("[bold green]You:[/] ");
						var prompt = System.Console.ReadLine();

						if (string.IsNullOrWhiteSpace(prompt))
						{
							continue;
						}

						if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
						{
							break;
						}

						try
						{
							Log.Debug("Sending prompt to Gemini: {Prompt}", prompt);
							var response = await chatService.GetResponse(prompt);

							// Display response with markdown formatting
							AnsiConsole.MarkupLine("[bold blue]Gemini:[/]");
							AnsiConsole.WriteLine();
							MarkdownRenderer.Render(response);
							AnsiConsole.WriteLine();
						}
						catch (Exception ex)
						{
							Log.Error(ex, "Error processing chat request");
							AnsiConsole.MarkupLine($"[red]An error occurred: {ex.Message}[/]");
							AnsiConsole.WriteLine();
						}
					}
				}

				Log.Information("OwnPlanner Console Chat ended");
			}
			catch (Exception ex)
			{
				Log.Fatal(ex, "Application terminated unexpectedly");
				AnsiConsole.MarkupLine($"[red]Fatal error: {ex.Message}[/]");
			}
			finally
			{
				await Log.CloseAndFlushAsync();
			}
		}
	}
}
