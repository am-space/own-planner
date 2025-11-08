using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Interfaces;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Mcp.StdioApp.Tools;

namespace OwnPlanner.Mcp.StdioApp
{
	internal class Program
	{
		static async Task Main(string[] args)
		{
			// Configure Serilog (send console logs to stderr to avoid interfering with MCP stdout)
			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
				.WriteTo.File("logs/stdioapp-.log", rollingInterval: RollingInterval.Day)
				.CreateLogger();

			var hostBuilder = Host.CreateDefaultBuilder(args)
				.UseSerilog()
				.ConfigureServices((context, services) =>
				{
					// DbContext - using Sqlite file in local folder for now
					var dbPath = Path.Combine(AppContext.BaseDirectory, "ownplanner.db");
					services.AddDbContext<AppDbContext>(options =>
						options.UseSqlite($"Data Source={dbPath}")
					);

					// Repositories
					services.AddScoped<TaskItemRepository>();
					services.AddScoped<OwnPlanner.Domain.Tasks.ITaskItemRepository, TaskItemRepository>();
					services.AddScoped<TaskListRepository>();
					services.AddScoped<OwnPlanner.Domain.Tasks.ITaskListRepository, TaskListRepository>();

					// Application services
					services.AddScoped<ITaskItemService, TaskItemService>();
					services.AddScoped<ITaskListService, TaskListService>();

					// MCP server (stdio transport + register tools via DI)
					services
						.AddMcpServer()
						.WithStdioServerTransport()
						.WithTools<TaskItemTools>()
						.WithTools<TaskListTools>();
				});

			var host = hostBuilder.Build();

			// Ensure database is created and migrations are applied
			using (var scope = host.Services.CreateScope())
			{
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
				await db.Database.MigrateAsync();
			}

			var logger = host.Services.GetRequiredService<ILogger<Program>>();
			logger.LogInformation("Starting MCP stdio server...");

			await host.RunAsync();
		}
	}
}
