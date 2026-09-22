namespace OwnPlanner.Application.WeeklyReviews;

/// <summary>Trusted host boundary for enumerating active accounts and executing inside their planner scope.</summary>
public interface IWeeklyReminderHost
{
	/// <summary>Enumerates server-owned account identities with currently linked delivery destinations.</summary>
	Task<IReadOnlyList<Guid>> GetLinkedUsersAsync(CancellationToken ct);
	/// <summary>Runs a callback inside the verified account's initialized planner scope; send rechecks its destination.</summary>
	Task WithUserAsync(Guid userId, Func<IWeeklyReviewService, Func<string, CancellationToken, Task>, Task> work, CancellationToken ct);
}
