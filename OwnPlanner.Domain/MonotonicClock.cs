namespace OwnPlanner.Domain;

/// <summary>
/// Supplies UTC timestamps that are guaranteed to be strictly increasing within a process.
/// <para>
/// <see cref="DateTime.UtcNow"/> has coarse resolution, so two entities created in quick
/// succession can receive identical <c>CreatedAt</c>/<c>UpdatedAt</c> values. That makes any
/// ordering by timestamp non-deterministic when values tie. This clock hands out monotonically
/// increasing values so creation/update order is always reflected in the audit timestamps.
/// </para>
/// </summary>
internal static class MonotonicClock
{
	private static long _lastTicks;

	/// <summary>
	/// Returns the current UTC time, advanced by at least one tick past the previously issued
	/// value when the system clock has not moved forward since the last call.
	/// </summary>
	public static DateTime UtcNow()
	{
		while (true)
		{
			var nowTicks = DateTime.UtcNow.Ticks;
			var lastTicks = Interlocked.Read(ref _lastTicks);
			var nextTicks = nowTicks > lastTicks ? nowTicks : lastTicks + 1;
			if (Interlocked.CompareExchange(ref _lastTicks, nextTicks, lastTicks) == lastTicks)
			{
				return new DateTime(nextTicks, DateTimeKind.Utc);
			}
		}
	}
}
