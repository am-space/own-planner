using System.Collections.Concurrent;

namespace OwnPlanner.Application.Usage;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IBurstRateLimiter"/>. Keeps, per user, the timestamps
/// of requests made in the last minute and prunes them on each access. Registered as a singleton so the
/// window survives across requests within the process.
/// </summary>
public sealed class BurstRateLimiter : IBurstRateLimiter
{
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

	private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _hits = new();

	public bool TryAcquire(Guid userId, int limitPerMinute, DateTimeOffset now, out int retryAfterSeconds)
	{
		retryAfterSeconds = 0;

		if (limitPerMinute <= 0)
		{
			return true;
		}

		var queue = _hits.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
		var cutoff = now - Window;

		// Lock the per-user queue: prune expired hits, then either reject or record this request.
		lock (queue)
		{
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
