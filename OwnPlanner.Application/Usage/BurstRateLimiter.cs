using System.Collections.Concurrent;

namespace OwnPlanner.Application.Usage;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IBurstRateLimiter"/>. Keeps, per user, the timestamps
/// of requests made in the last minute and prunes them on each access. Registered as a singleton so the
/// window survives across requests within the process. A background timer evicts users whose window has
/// emptied so the map does not grow unbounded over a long-running process with many distinct users.
/// </summary>
public sealed class BurstRateLimiter : IBurstRateLimiter, IDisposable
{
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

	private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _hits = new();
	private readonly Timer _cleanupTimer;

	/// <summary>Number of users currently tracked in memory. Exposed for tests to assert eviction.</summary>
	internal int TrackedUserCount => _hits.Count;

	public BurstRateLimiter()
	{
		// Sweep once per window; only entries that have gone fully idle (empty after pruning) are removed.
		_cleanupTimer = new Timer(_ => Evict(DateTimeOffset.UtcNow), state: null, Window, Window);
	}

	public bool TryAcquire(Guid userId, int limitPerMinute, DateTimeOffset now, out int retryAfterSeconds)
	{
		retryAfterSeconds = 0;

		if (limitPerMinute <= 0)
		{
			return true;
		}

		var cutoff = now - Window;

		while (true)
		{
			var queue = _hits.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());

			// Lock the per-user queue: prune expired hits, then either reject or record this request.
			lock (queue)
			{
				// Eviction may have removed this exact queue between GetOrAdd and acquiring the lock; if so the
				// reference is now orphaned (not in the map), so re-fetch rather than recording into a lost queue.
				if (!_hits.TryGetValue(userId, out var current) || !ReferenceEquals(current, queue))
				{
					continue;
				}

				while (queue.Count > 0 && queue.Peek() <= cutoff)
				{
					queue.Dequeue();
				}

				if (queue.Count >= limitPerMinute)
				{
					var oldest = queue.Peek();
					var secondsUntilFree = (oldest + Window - now).TotalSeconds;
					retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(secondsUntilFree));
					return false;
				}

				queue.Enqueue(now);
				return true;
			}
		}
	}

	/// <summary>
	/// Removes users whose window has fully emptied as of <paramref name="now"/>. Exposed internally so the
	/// sweep can be exercised deterministically in tests without waiting on the timer.
	/// </summary>
	internal void Evict(DateTimeOffset now)
	{
		var cutoff = now - Window;

		foreach (var (userId, queue) in _hits)
		{
			lock (queue)
			{
				while (queue.Count > 0 && queue.Peek() <= cutoff)
				{
					queue.Dequeue();
				}

				if (queue.Count == 0)
				{
					// Remove only this specific empty queue; a concurrent TryAcquire re-validates via its guard.
					_hits.TryRemove(new KeyValuePair<Guid, Queue<DateTimeOffset>>(userId, queue));
				}
			}
		}
	}

	public void Dispose() => _cleanupTimer.Dispose();
}
