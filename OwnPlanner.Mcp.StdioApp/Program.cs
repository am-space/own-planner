using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Application.Notes;
using OwnPlanner.Application.Notes.Interfaces;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Mcp.StdioApp.Tools;

namespace OwnPlanner.Mcp.StdioApp
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			// Parse session ID and user ID from command line arguments
			string? sessionId = null;
			string? userId = null;
			
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i] == "--session-id")
				{
					sessionId = args[i + 1];
				}
				else if (args[i] == "--user-id")
				{
					userId = args[i + 1];
				}
			}

			var dataDir = Environment.GetEnvironmentVariable("MCP_DATA_DIR") 
			              ?? AppContext.BaseDirectory;

			var logDir = Environment.GetEnvironmentVariable("MCP_LOG_DIR")
			             ?? Path.Combine(AppContext.BaseDirectory, "logs");
			
			var logFileName = string.IsNullOrEmpty(userId)
				? Path.Combine(logDir, "stdioapp-.log")
				: Path.Combine(logDir, $"stdioapp-user-{userId}-.log");

			// Configure Serilog (send console logs to stderr to avoid interfering with MCP stdout)
			var logConfig = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
				.WriteTo.File(logFileName, rollingInterval: RollingInterval.Day);

			// Enrich logs with session ID and user ID if provided
			if (!string.IsNullOrEmpty(sessionId))
			{
				logConfig = logConfig.Enrich.WithProperty("SessionId", sessionId);
			}
			if (!string.IsNullOrEmpty(userId))
			{
				logConfig = logConfig.Enrich.WithProperty("UserId", userId);
			}

			Log.Logger = logConfig.CreateLogger();

			Log.Information("MCP Server starting - SessionId: {SessionId}, UserId: {UserId}", 
				sessionId ?? "unknown", userId ?? "unknown");
			Log.Information("Data directory: {DataDir}", dataDir);
			Log.Information("Log directory: {LogDir}", logDir);

			var hostBuilder = Host.CreateDefaultBuilder(args)
				.UseSerilog()
				.ConfigureServices((context, services) =>
				{
					// Register session context as a singleton for access in tools
					services.AddSingleton(new SessionContext 
					{ 
						SessionId = sessionId ?? "unknown",
						UserId = userId ?? "unknown"
					});

					// DbContext - using per-user Sqlite file in the data directory
					var dbFileName = string.IsNullOrEmpty(userId) 
						? "ownplanner.db" 
						: $"ownplanner-user-{userId}.db";
					
					var dbPath = Path.Combine(dataDir, dbFileName);
					
					Log.Information("Using database: {DbPath}", dbPath);
					
					services.AddDbContext<AppDbContext>(options =>
						options.UseSqlite($"Data Source={dbPath}")
					);

					// Repositories
					services.AddScoped<TaskItemRepository>();
					services.AddScoped<OwnPlanner.Domain.Tasks.ITaskItemRepository, TaskItemRepository>();
					services.AddScoped<TaskListRepository>();
					services.AddScoped<OwnPlanner.Domain.Tasks.ITaskListRepository, TaskListRepository>();
					services.AddScoped<NoteListRepository>();
					services.AddScoped<OwnPlanner.Domain.Notes.INoteListRepository, NoteListRepository>();
					services.AddScoped<NoteItemRepository>();
					services.AddScoped<OwnPlanner.Domain.Notes.INoteItemRepository, NoteItemRepository>();

					// Application services
					services.AddScoped<ITaskItemService, TaskItemService>();
					services.AddScoped<ITaskListService, TaskListService>();
					services.AddScoped<INoteListService, NoteListService>();
					services.AddScoped<INoteItemService, NoteItemService>();

					// MCP server (stdio transport + register tools via DI)
					services
						.AddMcpServer()
						.WithStdioServerTransport()
						.WithTools<TaskItemTools>()
						.WithTools<TaskListTools>()
						.WithTools<NoteListTools>()
						.WithTools<NoteItemTools>()
						.WithTools<DateTimeTools>();
				});

			var host = hostBuilder.Build();

			// Ensure database is created and migrations are applied
			using (var scope = host.Services.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
				await db.Database.MigrateAsync();
			}

			var logger = host.Services.GetRequiredService<ILogger<Program>>();
			logger.LogInformation("MCP stdio server started successfully");

			await host.RunAsync();
		}
	}

	/// <summary>
	/// Context information for the current MCP session
	/// </summary>
	public class SessionContext
	{
		public required string SessionId { get; init; }
		public required string UserId { get; init; }
	}
}
