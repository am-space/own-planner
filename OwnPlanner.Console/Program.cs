using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OwnPlanner.Application.Chat;
using OwnPlanner.Infrastructure.Adapters;
using Serilog;
using Serilog.Extensions.Logging;
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
						// Escape square brackets to prevent Spectre.Console markup errors
						var safeMessage = ex.Message.Replace("[", "[[").Replace("]", "]]");
						AnsiConsole.MarkupLine($"[yellow]Warning: Failed to create MCP adapter: {safeMessage}[/]");
					}
				}
				
				AnsiConsole.MarkupLine("[dim]Type 'exit' to quit. Type '/mode <name>' to switch mode.[/]");
				AnsiConsole.MarkupLine($"[dim]Modes: {string.Join(", ", Enum.GetNames<PlanningMode>())}[/]");
				AnsiConsole.WriteLine();

				await using (mcpAdapter)
				{
					using var loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);
					var planningLogger = loggerFactory.CreateLogger<PlanningService>();

					var chatAdapter = new ChatServiceAdapter(
						settings.Gemini.ApiKey,
						settings.Gemini.Model,
						settings.Gemini.MaxToolCallRounds,
						mcpAdapter);

					await using var planningService = new PlanningService(
						chatAdapter,
						mcpAdapter,
						planningLogger);

					while (true)
					{
						AnsiConsole.Markup($"[bold green][[{planningService.CurrentMode}]] You:[/] ");
						var prompt = System.Console.ReadLine();

						if (string.IsNullOrWhiteSpace(prompt))
						{
							continue;
						}

						if (prompt.Equals("exit", StringComparison.OrdinalIgnoreCase))
						{
							break;
						}

						if (prompt.StartsWith("/mode", StringComparison.OrdinalIgnoreCase))
						{
							var parts = prompt.Split(' ', 2, StringSplitOptions.TrimEntries);
							if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
							{
								AnsiConsole.MarkupLine($"[dim]Current mode: {planningService.CurrentMode}[/]");
								AnsiConsole.MarkupLine($"[dim]Available: {string.Join(", ", Enum.GetNames<PlanningMode>())}[/]");
							}
							else if (!Enum.TryParse<PlanningMode>(parts[1], ignoreCase: true, out var mode))
							{
								AnsiConsole.MarkupLine($"[yellow]Unknown mode '{parts[1]}'. Available: {string.Join(", ", Enum.GetNames<PlanningMode>())}[/]");
							}
							else
							{
								try
								{
									await AnsiConsole.Status().StartAsync(
										$"Switching to {mode}, loading context...",
										async _ => await planningService.SwitchModeAsync(mode));
									AnsiConsole.MarkupLine($"[bold cyan]Mode switched to {mode}.[/]");
								}
								catch (Exception ex)
								{
									Log.Error(ex, "Error switching mode to {Mode}", mode);
									var safeMessage = ex.Message.Replace("[", "[[").Replace("]", "]]");
									AnsiConsole.MarkupLine($"[red]Failed to switch mode: {safeMessage}[/]");
								}
							}
							AnsiConsole.WriteLine();
							continue;
						}

						try
						{
							Log.Debug("Sending prompt to Gemini: {Prompt}", prompt);
							var response = await planningService.GetResponseAsync(prompt);

							// Display response with markdown formatting
							AnsiConsole.MarkupLine("[bold blue]Gemini:[/]");
							AnsiConsole.WriteLine();
                          MarkdownRenderer.Render(response.Message);
							AnsiConsole.WriteLine();
						}
						catch (Exception ex)
						{
							Log.Error(ex, "Error processing chat request");
							// Escape square brackets to prevent Spectre.Console markup errors
							var safeMessage = ex.Message.Replace("[", "[[").Replace("]", "]]");
							AnsiConsole.MarkupLine($"[red]An error occurred: {safeMessage}[/]");
							AnsiConsole.WriteLine();
						}
					}
				}

				Log.Information("OwnPlanner Console Chat ended");
			}
			catch (Exception ex)
			{
				Log.Fatal(ex, "Application terminated unexpectedly");
				// Escape square brackets to prevent Spectre.Console markup errors
				var safeMessage = ex.Message.Replace("[", "[[").Replace("]", "]]");
				AnsiConsole.MarkupLine($"[red]Fatal error: {safeMessage}[/]");
			}
			finally
			{
				await Log.CloseAndFlushAsync();
			}
		}
	}
}
