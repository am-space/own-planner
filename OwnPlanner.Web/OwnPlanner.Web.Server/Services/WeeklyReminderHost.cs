using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.WeeklyReviews;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

public sealed class WeeklyReminderHost(IServiceScopeFactory scopes, IPlannerSessionContextAccessor accessor,
	PerUserAppInitializationService initialization, IOptions<TelegramOptions> options) : IWeeklyReminderHost
{
	public async Task<IReadOnlyList<Guid>> GetLinkedUsersAsync(CancellationToken ct)
	{
		if (!options.Value.Enabled) return [];
		using var scope = scopes.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
		return await db.TelegramAccountLinks.Where(l => db.Users.Any(u => u.Id == l.UserId && u.IsActive))
			.Select(l => l.UserId).ToListAsync(ct);
	}

	public async Task WithUserAsync(Guid userId, Func<IWeeklyReviewService, Func<string, CancellationToken, Task>, Task> work, CancellationToken ct)
	{
		using var scope = scopes.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
		if (!options.Value.Enabled || !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct)) return;
		var link = await db.TelegramAccountLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId, ct);
		if (link is null) return;
		var context = new SessionContext { UserId = userId.ToString(), SessionId = "weekly-reminder" };
		await initialization.EnsureInitializedAsync(context, ct);
		using var userScope = accessor.BeginScope(context);
		var service = scope.ServiceProvider.GetRequiredService<IWeeklyReviewService>();
		await work(service, async (text, token) =>
		{
			// A removed/replaced link must never redirect an already claimed message to a different destination.
			if (!options.Value.Enabled || !await db.TelegramAccountLinks.AnyAsync(l => l.Id == link.Id && l.UserId == userId && l.ChatId == link.ChatId &&
				db.Users.Any(u => u.Id == userId && u.IsActive), token) || !(await service.GetPreferencesAsync(token)).Enabled)
				throw new InvalidOperationException("Reminder destination is no longer available.");
			await scope.ServiceProvider.GetRequiredService<ITelegramBotClient>().SendTextAsync(link.ChatId, text, token);
		});
	}
}

public sealed class WeeklyReminderWorker(WeeklyReminderDispatcher dispatcher, TimeProvider clock,
	ILogger<WeeklyReminderWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Let the host finish central database initialization before the first tick.
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try { await dispatcher.RunOnceAsync(stoppingToken); }
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
			catch (Exception ex) { logger.LogWarning("Weekly reminder tick failed ({FailureType})", ex.GetType().Name); }
		}
	}
}
