using Serilog;
using OwnPlanner.Web.Server.Middleware;

namespace OwnPlanner.Web.Server
{
	public class Program
	{
		public static void Main(string[] args)
		{
			// Configure Serilog early to capture startup logs
			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
				.MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
				.MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", Serilog.Events.LogEventLevel.Fatal) // Suppress duplicate exception logs
				.Enrich.FromLogContext()
				.WriteTo.Console()
				.WriteTo.File("logs/web-.log", rollingInterval: RollingInterval.Day)
				.CreateLogger();

			try
			{
				Log.Information("Starting OwnPlanner Web Server");

				var builder = WebApplication.CreateBuilder(args);

				// Use Serilog for logging
				builder.Host.UseSerilog();

				// Add services to the container.

				// Register global exception handler
				builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
				builder.Services.AddProblemDetails();

				builder.Services.AddControllers();
				// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
				builder.Services.AddOpenApi();

				var app = builder.Build();

				// Place Serilog request logging at the start so handled exceptions don't log twice
				app.UseSerilogRequestLogging(options =>
				{
					options.GetLevel = (httpContext, elapsed, ex) => ex != null
						? Serilog.Events.LogEventLevel.Warning  // Log requests with exceptions at Warning level
						: httpContext.Response.StatusCode >= 500
							? Serilog.Events.LogEventLevel.Warning
							: Serilog.Events.LogEventLevel.Information;
					
					// Customize message template to exclude exception details
					options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
				});

				// Use global exception handler to format error responses and log details
				app.UseExceptionHandler();

				app.UseDefaultFiles();
				app.MapStaticAssets();

				// Configure the HTTP request pipeline.
				if (app.Environment.IsDevelopment())
				{
					app.MapOpenApi();
				}

				app.UseHttpsRedirection();

				app.UseAuthorization();

				app.MapControllers();

				app.MapFallbackToFile("/index.html");

				Log.Information("OwnPlanner Web Server started successfully");

				app.Run();
			}
			catch (Exception ex)
			{
				Log.Fatal(ex, "Application terminated unexpectedly");
				throw;
			}
			finally
			{
				Log.CloseAndFlush();
			}
		}
	}
}
