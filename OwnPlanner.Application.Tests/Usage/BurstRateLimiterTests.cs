using FluentAssertions;
using OwnPlanner.Application.Usage;

namespace OwnPlanner.Application.Tests.Usage;

public class BurstRateLimiterTests
{
	private static readonly DateTimeOffset T0 = new(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void TryAcquire_AllowsUpToLimit_ThenRejects()
	{
		var limiter = new BurstRateLimiter();
		var user = Guid.NewGuid();

		limiter.TryAcquire(user, 3, T0, out _).Should().BeTrue();
		limiter.TryAcquire(user, 3, T0, out _).Should().BeTrue();
		limiter.TryAcquire(user, 3, T0, out _).Should().BeTrue();

		limiter.TryAcquire(user, 3, T0, out var retryAfter).Should().BeFalse();
		retryAfter.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(60);
	}

	[Fact]
	public void TryAcquire_WithNonPositiveLimit_AlwaysAllows()
	{
		var limiter = new BurstRateLimiter();
		var user = Guid.NewGuid();

		for (var i = 0; i < 100; i++)
		{
			limiter.TryAcquire(user, 0, T0, out _).Should().BeTrue();
		}
	}

	[Fact]
	public void TryAcquire_WindowSlides_AllowsAgainAfterOldestAgesOut()
	{
		var limiter = new BurstRateLimiter();
		var user = Guid.NewGuid();

		limiter.TryAcquire(user, 1, T0, out _).Should().BeTrue();
		// Still within the one-minute window -> rejected.
		limiter.TryAcquire(user, 1, T0.AddSeconds(30), out _).Should().BeFalse();
		// The first hit has aged out of the window -> allowed again.
		limiter.TryAcquire(user, 1, T0.AddSeconds(61), out _).Should().BeTrue();
	}

	[Fact]
	public void TryAcquire_IsolatesUsers()
	{
		var limiter = new BurstRateLimiter();
		var userA = Guid.NewGuid();
		var userB = Guid.NewGuid();

		limiter.TryAcquire(userA, 1, T0, out _).Should().BeTrue();
		limiter.TryAcquire(userA, 1, T0, out _).Should().BeFalse();
		// A different user has an independent window.
		limiter.TryAcquire(userB, 1, T0, out _).Should().BeTrue();
	}
}
