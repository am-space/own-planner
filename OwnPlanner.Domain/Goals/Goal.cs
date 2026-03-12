namespace OwnPlanner.Domain.Goals;

/// <summary>
/// A time-bounded intention or outcome. Sits at the top of the planning hierarchy —
/// not owned by any <see cref="OwnPlanner.Domain.Contexts.PlanningContext"/>.
/// Goals give direction to work done inside contexts and can be linked to individual tasks.
/// </summary>
public class Goal : EntityBase
{
	public string Title { get; private set; } = string.Empty;
	public string? Description { get; private set; }

	/// <summary>The time granularity of this goal (monthly, quarterly, yearly, or a fixed date).</summary>
	public GoalHorizon Horizon { get; private set; }

	/// <summary>
	/// Human-readable period string, e.g. <c>2025-06</c>, <c>2025-Q2</c>, <c>2025</c>.
	/// Set when <see cref="Horizon"/> is not <see cref="GoalHorizon.TargetDate"/>; otherwise <c>null</c>.
	/// </summary>
	public string? TargetPeriod { get; private set; }

	/// <summary>
	/// Hard deadline. Set only when <see cref="Horizon"/> is <see cref="GoalHorizon.TargetDate"/>; otherwise <c>null</c>.
	/// </summary>
	public DateTime? TargetDate { get; private set; }

	/// <summary>Lifecycle status: active work-in-progress, successfully achieved, or dropped.</summary>
	public GoalStatus Status { get; private set; }

	/// <summary>Optional description of the measurable outcome, e.g. "Run 5 km without stopping".</summary>
	public string? Metric { get; private set; }

	/// <summary>Optional current progress value against <see cref="Metric"/>, e.g. "3.2 km".</summary>
	public string? MetricCurrent { get; private set; }

	// EF Core constructor
	private Goal() { }

	public Goal(string title, GoalHorizon horizon, string? description = null, string? targetPeriod = null, DateTime? targetDate = null, string? metric = null)
		: base(Guid.NewGuid())
	{
		SetTitle(title);
		SetDescription(description);
		SetHorizon(horizon, targetPeriod, targetDate);
		SetMetric(metric);
		Status = GoalStatus.Active;
	}

	public void SetTitle(string title)
	{
		if (string.IsNullOrWhiteSpace(title))
			throw new ArgumentException("Title is required", nameof(title));
		Title = title.Trim();
		Touch();
	}

	public void SetDescription(string? description)
	{
		Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		Touch();
	}

	/// <summary>
	/// Updates the planning horizon and enforces mutual exclusivity:
	/// <see cref="GoalHorizon.TargetDate"/> requires a <paramref name="targetDate"/> and clears <see cref="TargetPeriod"/>;
	/// all other horizons require no date and store an optional <paramref name="targetPeriod"/> string.
	/// </summary>
	public void SetHorizon(GoalHorizon horizon, string? targetPeriod, DateTime? targetDate)
	{
		if (horizon == GoalHorizon.TargetDate)
		{
			if (targetDate is null)
				throw new ArgumentException("TargetDate is required when Horizon is TargetDate", nameof(targetDate));
			Horizon = horizon;
			TargetPeriod = null;
			TargetDate = DateTime.SpecifyKind(targetDate.Value, DateTimeKind.Utc);
		}
		else
		{
			Horizon = horizon;
			TargetPeriod = string.IsNullOrWhiteSpace(targetPeriod) ? null : targetPeriod.Trim();
			TargetDate = null;
		}
		Touch();
	}

	public void SetStatus(GoalStatus status)
	{
		Status = status;
		Touch();
	}

	public void SetMetric(string? metric)
	{
		Metric = string.IsNullOrWhiteSpace(metric) ? null : metric.Trim();
		Touch();
	}

	public void SetMetricCurrent(string? metricCurrent)
	{
		MetricCurrent = string.IsNullOrWhiteSpace(metricCurrent) ? null : metricCurrent.Trim();
		Touch();
	}
}
