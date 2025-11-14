using Serilog;
using OwnPlanner.Web.Server.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Application.Auth.Interfaces;
using OwnPlanner.Application.Auth;

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

				// Configure authentication database (users, auth data)
				// Use configured path or default to ContentRootPath
				var configuredAuthDbPath = builder.Configuration["Database:AuthDbPath"];
				var authDbPath = string.IsNullOrWhiteSpace(configuredAuthDbPath)
					? Path.Combine(builder.Environment.ContentRootPath, "ownplanner-auth.db")
					: Path.GetFullPath(configuredAuthDbPath);

				Log.Information("Database path configured: {AuthDbPath}", authDbPath);

				builder.Services.AddDbContext<AuthDbContext>(options =>
					options.UseSqlite($"Data Source={authDbPath}")
				);

				// Register repositories
				builder.Services.AddScoped<IUserRepository, UserRepository>();

				// Register application services
				builder.Services.AddScoped<IAuthService, AuthService>();

				// Configure cookie authentication
				builder.Services
					.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
					.AddCookie(options =>
					{
						options.Cookie.Name = "OwnPlanner.Auth";
						options.Cookie.HttpOnly = true;
						options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
						options.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict;
						options.ExpireTimeSpan = TimeSpan.FromDays(7);
						options.SlidingExpiration = true;
						
						// Return 401 instead of redirecting to login page for API calls
						options.Events.OnRedirectToLogin = context =>
						{
							context.Response.StatusCode = StatusCodes.Status401Unauthorized;
							return Task.CompletedTask;
						};
						
						options.Events.OnRedirectToAccessDenied = context =>
						{
							context.Response.StatusCode = StatusCodes.Status403Forbidden;
							return Task.CompletedTask;
						};
					});

				builder.Services.AddAuthorization();

				// Register global exception handler
				builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
				builder.Services.AddProblemDetails();

				builder.Services.AddControllers();
				// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
				builder.Services.AddOpenApi();

				var app = builder.Build();

				// Ensure authentication database is created and migrations are applied
				using (var scope = app.Services.CreateScope())
				{
					var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
					authDb.Database.Migrate();
					Log.Information("Authentication database initialized at: {DbPath}", authDbPath);
				}

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

				// Authentication & Authorization middleware (must be in this order)
				app.UseAuthentication();
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
