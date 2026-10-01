namespace OwnPlanner.Application.Goals;

/// <summary>Advisory guidance shared by successful goal actions and weekly review openings.</summary>
public static class GoalFocusGuidance
{
	public const int RecommendedActiveLimit = 5;

	public static string? Warning(int activeCount) => activeCount > RecommendedActiveLimit
		? $"You now have {activeCount} active goals. Focusing on up to {RecommendedActiveLimit} usually works better. Pause or drop one?"
		: null;
}
