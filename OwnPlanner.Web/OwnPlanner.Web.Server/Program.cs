using Serilog;
using OwnPlanner.Web.Server.Middleware;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Account;
using OwnPlanner.Application.Contexts;
using OwnPlanner.Infrastructure.Account;
using OwnPlanner.Infrastructure.Adapters;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Planner;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Application.Auth;
using OwnPlanner.Application.Email;
using OwnPlanner.Application.Goals;
using OwnPlanner.Application.Inbox;
using OwnPlanner.Application.Notes;
using OwnPlanner.Application.Planner;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Usage;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Mcp.Tools;
using OwnPlanner.Web.Server.Authentication;
using OwnPlanner.Web.Server.Configuration;
using OwnPlanner.Web.Server.Services;
using OwnPlanner.Infrastructure.Telegram;

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
				var configuredUserDbDirectory = builder.Configuration["Database:UserDbDirectory"];
				var userDbDirectory = string.IsNullOrWhiteSpace(configuredUserDbDirectory)
					? Path.GetFullPath(Environment.GetEnvironmentVariable("MCP_DATA_DIR") ?? Path.Combine(builder.Environment.ContentRootPath, "data", "databases"))
					: Path.GetFullPath(configuredUserDbDirectory);

				Directory.CreateDirectory(userDbDirectory);
				Log.Information("Planner user database directory configured: {UserDbDirectory}", userDbDirectory);

				builder.Services.AddDbContext<AuthDbContext>(options =>
					options.UseSqlite($"Data Source={authDbPath}")
				);
				builder.Services.AddHttpContextAccessor();
				builder.Services.AddSingleton<IPlannerSessionContextAccessor, PlannerSessionContextAccessor>();
				builder.Services.AddSingleton<PerUserAppInitializationService>();
				builder.Services.AddSingleton<IPerUserAppInitializationService>(sp => sp.GetRequiredService<PerUserAppInitializationService>());
				builder.Services.AddTransient<McpRequestInitializationMiddleware>();
				builder.Services.AddScoped<SessionContext>(sp =>
				{
					var sessionContextAccessor = sp.GetRequiredService<IPlannerSessionContextAccessor>();
					return sessionContextAccessor.Current ?? CreateSessionContext(sp.GetRequiredService<IHttpContextAccessor>().HttpContext);
				});
				builder.Services.AddScoped<IPlannerDbContextFactory>(serviceProvider =>
					new PlannerAppDbContextFactory(
						userDbDirectory,
						serviceProvider.GetRequiredService<IPlannerSessionContextAccessor>(),
						serviceProvider.GetRequiredService<IHttpContextAccessor>(),
						serviceProvider.GetRequiredService<ILogger<PlannerAppDbContextFactory>>()));

				// Register repositories
				builder.Services.AddScoped<IUserRepository, UserRepository>();
				builder.Services.AddScoped<IPersonalAccessTokenRepository, PersonalAccessTokenRepository>();
				builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
				builder.Services.AddScoped<ITaskItemRepository, TaskItemRepository>();
				builder.Services.AddScoped<ITaskListRepository, TaskListRepository>();
				builder.Services.AddScoped<INoteListRepository, NoteListRepository>();
				builder.Services.AddScoped<INoteItemRepository, NoteItemRepository>();
				builder.Services.AddScoped<IGoalRepository, GoalRepository>();
				builder.Services.AddScoped<IPlanningContextRepository, PlanningContextRepository>();
				builder.Services.AddScoped<IUserDailyUsageRepository, UserDailyUsageRepository>();
				builder.Services.AddScoped<IUserQuotaOverrideRepository, UserQuotaOverrideRepository>();
				builder.Services.AddScoped<IPlannerReadStore, PlannerReadStore>();
				builder.Services.AddSingleton(TimeProvider.System);
				builder.Services.AddScoped<OwnPlanner.Application.Reporting.IStrategicReportReader, OwnPlanner.Infrastructure.Reporting.StrategicReportReader>();

				// Register application services
				builder.Services.AddScoped<IAuthService, AuthService>();
				builder.Services.AddScoped<ITaskItemService, TaskItemService>();
				builder.Services.AddScoped<ITaskListService, TaskListService>();
				builder.Services.AddScoped<INoteListService, NoteListService>();
				builder.Services.AddScoped<INoteItemService, NoteItemService>();
				builder.Services.AddScoped<IGoalService, GoalService>();
				builder.Services.AddScoped<IPlanningContextService, PlanningContextService>();
				builder.Services.AddScoped<IInboxSeeder, InboxSeeder>();
				builder.Services.AddScoped<IUsageQuotaService, UsageQuotaService>();
				builder.Services.AddScoped<IAccountExportService, AccountExportService>();
				builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
				builder.Services.AddScoped<IPlannerReadService, PlannerReadService>();
				builder.Services.AddHostedService<ExportTempFileCleanupService>();

				var telegramOptions = builder.Configuration.GetSection("Telegram").Get<TelegramOptions>() ?? new TelegramOptions();
				builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
				if (telegramOptions.Enabled)
				{
					var missing = new List<string>();
					if (string.IsNullOrWhiteSpace(telegramOptions.BotToken)) missing.Add("Telegram:BotToken");
					if (string.IsNullOrWhiteSpace(telegramOptions.BotUsername)) missing.Add("Telegram:BotUsername");
					if (string.IsNullOrWhiteSpace(telegramOptions.WebhookSecret)) missing.Add("Telegram:WebhookSecret");
					if (missing.Count > 0) throw new InvalidOperationException($"Missing required Telegram settings: {string.Join(", ", missing)}.");
				}
				builder.Services.AddScoped<ITelegramIntegrationService, TelegramIntegrationService>();
				builder.Services.AddSingleton<TelegramChatLock>();
				builder.Services.AddScoped<TelegramWebhookSecretFilter>();
				// Telegram requires the bot token in the request path. Use a dedicated client without the
				// IHttpClientFactory logging handlers so request logs can never expose that credential.
				builder.Services.AddSingleton<ITelegramBotClient>(sp => new TelegramBotClient(
					new HttpClient { BaseAddress = new Uri("https://api.telegram.org/") },
					sp.GetRequiredService<IOptions<TelegramOptions>>()));

				// Configure usage quota: bound limits (singleton instance) + in-memory burst window (singleton)
				var usageQuotaOptions = builder.Configuration.GetSection("UsageQuota").Get<UsageQuotaOptions>() ?? new UsageQuotaOptions();
				builder.Services.AddSingleton(usageQuotaOptions);
				builder.Services.AddSingleton<IBurstRateLimiter, BurstRateLimiter>();

				// Configure email: bound options (singleton instance) + sender chosen by provider.
				// Provider defaults to "Logging" in Development and "Smtp" otherwise.
				var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
				builder.Services.AddSingleton(emailOptions);

				var emailProvider = builder.Configuration["Email:Provider"];
				if (string.IsNullOrWhiteSpace(emailProvider))
				{
					emailProvider = builder.Environment.IsDevelopment() ? "Logging" : "Smtp";
				}

				if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
				{
					// Fail fast rather than silently failing to deliver (or falling back to
					// a logging sender that would write reset tokens to logs) in production.
					if (string.IsNullOrWhiteSpace(emailOptions.Host) || string.IsNullOrWhiteSpace(emailOptions.FromAddress))
					{
						throw new InvalidOperationException(
							"Email:Host and Email:FromAddress are required when Email:Provider is 'Smtp'.");
					}

					builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
				}
				else
				{
					builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
				}

				// Configure chat settings
				builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("Chat"));
				
				// Register chat services
				builder.Services.AddSingleton<IChatAdapterFactory, GeminiChatAdapterFactory>();
				builder.Services.AddSingleton<IChatServiceFactory, ChatServiceFactory>();
				builder.Services.AddSingleton<IChatSessionManager, ChatSessionManager>();

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
						
						// Reject cookies whose user has been deleted or deactivated, so erasure takes
						// effect across all of the user's sessions (re-checked at most once per
						// validation interval).
						options.Events.OnValidatePrincipal = CookiePrincipalValidator.ValidateAsync;

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
					})
					.AddScheme<AuthenticationSchemeOptions, McpBearerAuthenticationHandler>(
						McpBearerAuthenticationDefaults.AuthenticationScheme,
						_ => { });

				builder.Services.AddAuthorization(options =>
				{
					options.AddPolicy(McpBearerAuthenticationDefaults.AuthorizationPolicy, policy =>
					{
						policy.AddAuthenticationSchemes(McpBearerAuthenticationDefaults.AuthenticationScheme);
						policy.RequireAuthenticatedUser();
					});
				});

				// Note: the MCP SDK serializes tool output through a frozen
				// JsonSerializerOptions singleton that ASCII-escapes non-ASCII, and it cannot be
				// reconfigured (confirmed on 1.1.0 and 1.4.0). List payloads are kept small via the
				// slim, paginated projection in the tool layer rather than via the serializer here.
				builder.Services
					.AddMcpServer()
					.WithHttpTransport()
					.WithTools<TaskItemTools>()
					.WithTools<TaskListTools>()
					.WithTools<NoteListTools>()
					.WithTools<NoteItemTools>()
					.WithTools<GoalTools>()
					.WithTools<PlanningContextTools>()
					.WithTools<StrategicReportTools>()
					.WithTools<DateTimeTools>();

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
					options.GetLevel = (httpContext, _, ex) => ex != null
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
				app.UseWhen(
					context => context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase),
					branch => branch.UseMiddleware<McpRequestInitializationMiddleware>());

				app.MapControllers();
				app.MapMcp("/mcp")
					.RequireAuthorization(McpBearerAuthenticationDefaults.AuthorizationPolicy);

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

		private static SessionContext CreateSessionContext(HttpContext? httpContext)
		{
			var userId = ResolveAuthenticatedUserId(httpContext);
			var sessionId = httpContext?.User.FindFirstValue("SessionId");

			return new SessionContext
			{
				SessionId = string.IsNullOrWhiteSpace(sessionId) ? $"http-{userId}" : sessionId,
				UserId = userId
			};
		}

		private static string ResolveAuthenticatedUserId(HttpContext? httpContext)
		{
			var userId = httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
			{
				throw new UnauthorizedAccessException("Authenticated user id is required for planner data access.");
			}

			return userId;
		}
	}
}
