using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using OwnPlanner.Application.Contexts;
using OwnPlanner.Application.Goals;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Notes;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Reporting;
using OwnPlanner.Mcp.Tools;

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
				.ConfigureServices((_, services) =>
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
					services.AddScoped<IPlannerDbContextFactory>(_ => new FixedPathPlannerDbContextFactory(dbPath));

					// Repositories
					services.AddScoped<ITaskItemRepository, TaskItemRepository>();
					services.AddScoped<ITaskListRepository, TaskListRepository>();
					services.AddScoped<INoteListRepository, NoteListRepository>();
					services.AddScoped<INoteItemRepository, NoteItemRepository>();

					services.AddScoped<IGoalRepository, GoalRepository>();
					services.AddScoped<IPlanningContextRepository, PlanningContextRepository>();

					// Application services
					services.AddScoped<ITaskItemService, TaskItemService>();
					services.AddScoped<ITaskListService, TaskListService>();
					services.AddScoped<INoteListService, NoteListService>();
					services.AddScoped<INoteItemService, NoteItemService>();

					services.AddScoped<IGoalService, GoalService>();
					services.AddScoped<IPlanningContextService, PlanningContextService>();
					services.AddSingleton(TimeProvider.System);
					services.AddScoped<IStrategicReportReader, StrategicReportReader>();
					services.AddScoped<IWeeklyReportReader, WeeklyReportReader>();

					// Inbox seeder
					services.AddScoped<IInboxSeeder, InboxSeeder>();

					// MCP server (stdio transport + register tools via DI).
					// Note: the MCP SDK serializes the final JSON-RPC message through a frozen
					// JsonSerializerOptions singleton that ASCII-escapes non-ASCII. Per-tool
					// WithTools(options) governs only result→content conversion, not the wire encoder,
					// so it cannot fix escaping (confirmed on 1.1.0 and 1.4.0). List payloads are kept
					// small via the slim, paginated projection in the tool layer instead.
					services
						.AddMcpServer()
						.WithStdioServerTransport()
						.WithTools<TaskItemTools>()
						.WithTools<TaskListTools>()
						.WithTools<NoteListTools>()
						.WithTools<NoteItemTools>()
						.WithTools<GoalTools>()
						.WithTools<PlanningContextTools>()
						.WithTools<StrategicReportTools>()
						.WithTools<WeeklyReportTools>()
						.WithTools<DateTimeTools>();
				});

			var host = hostBuilder.Build();

			// Ensure database is created and migrations are applied
			using (var scope = host.Services.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
				await db.Database.MigrateAsync();
			}

			// Seed system lists (Inbox TaskList and Inbox NoteList)
			using (var scope = host.Services.CreateScope())
			{
				var seeder = scope.ServiceProvider.GetRequiredService<IInboxSeeder>();
				await seeder.SeedAsync();
			}

			var logger = host.Services.GetRequiredService<ILogger<Program>>();
			logger.LogInformation("MCP stdio server started successfully");

			await host.RunAsync();
		}
	}
}
