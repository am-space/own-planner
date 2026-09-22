using OwnPlanner.Application.Telegram;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

/// <summary>Explicit review commands work in every Telegram mode without changing that mode.</summary>
public sealed class WeeklyReviewTelegramHandler(IServiceScopeFactory scopes, IPlannerSessionContextAccessor accessor,
	PerUserAppInitializationService initialization)
{
	public async Task<string> HandleAsync(TelegramLinkedAccount account, string arguments, CancellationToken ct)
	{
		var context = new SessionContext { UserId = account.UserId.ToString(), SessionId = "telegram-weekly-review" };
		await initialization.EnsureInitializedAsync(context, ct);
		using var userScope = accessor.BeginScope(context);
		using var scope = scopes.CreateScope();
		var service = scope.ServiceProvider.GetRequiredService<IWeeklyReviewService>();
		var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		try
		{
			if (parts.FirstOrDefault() == "enable" && parts.Length == 4 && int.TryParse(parts[2], out var weekStart))
			{
				await service.ConfigureAsync(true, parts[1], weekStart, parts[3], ct: ct);
				return "Weekly reminders enabled. Use /review to open the shared review.";
			}
			if (parts.FirstOrDefault() == "disable")
			{
				var preferences = await service.GetPreferencesAsync(ct);
				await service.ConfigureAsync(false, preferences.TimeZoneId, preferences.WeekStart, preferences.ReminderTime, preferences.Channel, ct);
				return "Weekly reminders disabled. Your reviews remain available on request.";
			}
			if (parts.Length > 0 && parts[0] is not ("finish" or "skip" or "defer")) return Help;
			var view = await service.OpenAsync(ct: ct);
			if (parts.Length > 0)
			{
				var action = parts[0] == "finish" ? "complete" : parts[0];
				var state = await service.TransitionAsync(view.Review.Id, action, parts.ElementAtOrDefault(1), ct);
				return $"Review for {state.TargetWeek:yyyy-MM-dd}: {state.Status}.";
			}
			return $"Weekly review: {view.Review.TargetWeek:yyyy-MM-dd} ({view.Review.TimeZoneId}), {view.Review.Status}.\n" +
				WeeklyReviewCalendar.Notification(view.Review.TargetWeek, view.Report) + "\n" +
				string.Join("\n", view.Report.Tasks.Select(t => $"• {t.Title}")) +
				$"\nShowing {view.Report.Tasks.Count} of {view.Report.TotalCount}. Continue in General or Week Planning chat to discuss and apply changes.\n" + Help;
		}
		catch (ArgumentException ex) { return ex.Message + "\n" + Help; }
	}

	private const string Help = "Commands: /review, /review finish, /review skip, /review defer yyyy-MM-ddTHH:mm (in the review timezone), /review disable, /review enable <timezone> <weekStart 0=Sun..6=Sat> <HH:mm>. Example: /review enable Europe/London 1 18:00. No task changes are made by these commands.";
}
