namespace OwnPlanner.Domain.Users;

/// <summary>
/// Tracks a single user's API usage for one UTC day: the number of chat requests made and the
/// Gemini token counts consumed. Request counts drive quota enforcement; token counts are a
/// backstop recorded for cost calibration (not yet enforced). One row per (user, day).
/// </summary>
public class UserDailyUsage : EntityBase
{
	public Guid UserId { get; private set; }

	/// <summary>The UTC day this usage row aggregates.</summary>
	public DateOnly Date { get; private set; }

	/// <summary>Number of chat requests the user has made on <see cref="Date"/>.</summary>
	public int RequestCount { get; private set; }

	/// <summary>Total Gemini prompt (input) tokens consumed on <see cref="Date"/>.</summary>
	public long InputTokens { get; private set; }

	/// <summary>Total Gemini candidate (output) tokens consumed on <see cref="Date"/>.</summary>
	public long OutputTokens { get; private set; }

	// EF Core constructor
	private UserDailyUsage() { }

	public UserDailyUsage(Guid userId, DateOnly date)
		: base(Guid.NewGuid())
	{
		if (userId == Guid.Empty)
			throw new ArgumentException("User id is required", nameof(userId));

		UserId = userId;
		Date = date;
	}

	/// <summary>Records one additional chat request for the day.</summary>
	public void IncrementRequest()
	{
		RequestCount++;
		Touch();
	}

	/// <summary>Adds the token counts from a completed chat turn to the day's totals.</summary>
	public void AddTokens(long inputTokens, long outputTokens)
	{
		if (inputTokens < 0)
			throw new ArgumentOutOfRangeException(nameof(inputTokens), "Token count cannot be negative.");
		if (outputTokens < 0)
			throw new ArgumentOutOfRangeException(nameof(outputTokens), "Token count cannot be negative.");

		InputTokens += inputTokens;
		OutputTokens += outputTokens;
		Touch();
	}
}
