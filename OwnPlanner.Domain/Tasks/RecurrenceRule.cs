namespace OwnPlanner.Domain.Tasks;

public enum RecurrenceFrequency
{
	Weekly,
	Monthly
}

public enum MissedOccurrencePolicy
{
	SkipExpiredPeriods,
	RetainEachObligation
}

/// <summary>Immutable calendar rule. Weekly alignment retains the week start selected at creation.</summary>
public sealed record RecurrenceRule
{
	public RecurrenceFrequency Frequency { get; }
	public int Interval { get; }
	public DayOfWeek WeekStart { get; }
	public IReadOnlyList<DayOfWeek> Weekdays { get; }
	public IReadOnlyList<int> MonthDays { get; }

	private RecurrenceRule(RecurrenceFrequency frequency, int interval, DayOfWeek weekStart,
		IReadOnlyList<DayOfWeek> weekdays, IReadOnlyList<int> monthDays)
	{
		if (interval < 1) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
		Frequency = frequency;
		Interval = interval;
		WeekStart = weekStart;
		Weekdays = weekdays;
		MonthDays = monthDays;
	}

	/// <summary>Creates a weekly rule aligned to the configured week start at creation, independent of later preferences.</summary>
	public static RecurrenceRule Weekly(int interval, IEnumerable<DayOfWeek> weekdays, DayOfWeek weekStart)
	{
		ArgumentNullException.ThrowIfNull(weekdays);
		if (!Enum.IsDefined(weekStart)) throw new ArgumentOutOfRangeException(nameof(weekStart));
		var days = weekdays.Distinct().OrderBy(day => (7 + (int)day - (int)weekStart) % 7).ToArray();
		if (days.Length == 0 || days.Any(day => !Enum.IsDefined(day)))
			throw new ArgumentException("Select at least one valid weekday.", nameof(weekdays));
		return new(RecurrenceFrequency.Weekly, interval, weekStart, Array.AsReadOnly(days), Array.Empty<int>());
	}

	/// <summary>Preserves selected month days; calculation clamps and deduplicates separately in each eligible month.</summary>
	public static RecurrenceRule Monthly(int interval, IEnumerable<int> monthDays)
	{
		ArgumentNullException.ThrowIfNull(monthDays);
		var days = monthDays.Distinct().Order().ToArray();
		if (days.Length == 0 || days.Any(day => day is < 1 or > 31))
			throw new ArgumentException("Select at least one month day from 1 through 31.", nameof(monthDays));
		return new(RecurrenceFrequency.Monthly, interval, DayOfWeek.Monday, Array.Empty<DayOfWeek>(), Array.AsReadOnly(days));
	}

	public bool Equals(RecurrenceRule? other) => other is not null && Frequency == other.Frequency &&
		Interval == other.Interval && WeekStart == other.WeekStart &&
		Weekdays.SequenceEqual(other.Weekdays) && MonthDays.SequenceEqual(other.MonthDays);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Frequency);
		hash.Add(Interval);
		hash.Add(WeekStart);
		foreach (var day in Weekdays) hash.Add(day);
		foreach (var day in MonthDays) hash.Add(day);
		return hash.ToHashCode();
	}
}
