using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OwnPlanner.Application.Usage;
using OwnPlanner.Domain.Users;

namespace OwnPlanner.Application.Tests.Usage;

public class UsageQuotaServiceTests
{
	private readonly IUserDailyUsageRepository _dailyUsage = Substitute.For<IUserDailyUsageRepository>();
	private readonly IUserQuotaOverrideRepository _overrides = Substitute.For<IUserQuotaOverrideRepository>();
	private readonly IBurstRateLimiter _burst = Substitute.For<IBurstRateLimiter>();
	private readonly ILogger<UsageQuotaService> _logger = Substitute.For<ILogger<UsageQuotaService>>();
	private readonly string _userId = Guid.NewGuid().ToString();

	private UsageQuotaService CreateService(UsageQuotaOptions? options = null)
		=> new(_dailyUsage, _overrides, _burst, options ?? new UsageQuotaOptions(), _logger);

	private void AllowBurst()
		=> _burst.TryAcquire(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), out Arg.Any<int>())
			.Returns(call => { call[3] = 0; return true; });

	[Fact]
	public async Task CheckAndReserve_UnderDailyLimit_ReservesAndReturnsRemaining()
	{
		var ct = TestContext.Current.CancellationToken;
		AllowBurst();
		_dailyUsage.IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct).Returns(5);
		var service = CreateService(new UsageQuotaOptions { DailyRequestLimit = 200, BurstRequestsPerMinute = 10 });

		var status = await service.CheckAndReserveAsync(_userId, ct);

		status.DailyLimit.Should().Be(200);
		status.Used.Should().Be(5);
		status.Remaining.Should().Be(195);
		status.ResetAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
		await _dailyUsage.Received(1).IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct);
	}

	[Fact]
	public async Task CheckAndReserve_OverDailyLimit_ThrowsDaily_ButStillConsumesTheRequest()
	{
		var ct = TestContext.Current.CancellationToken;
		AllowBurst();
		_dailyUsage.IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct).Returns(201);
		var service = CreateService(new UsageQuotaOptions { DailyRequestLimit = 200 });

		var act = () => service.CheckAndReserveAsync(_userId, ct);

		var ex = (await act.Should().ThrowAsync<UsageQuotaExceededException>()).Which;
		ex.LimitKind.Should().Be(UsageLimitKind.Daily);
		ex.RetryAfterSeconds.Should().BeGreaterThan(0);
		ex.Remaining.Should().Be(0);
		ex.ResetAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
		// No refund: the counter was incremented even though the request is rejected.
		await _dailyUsage.Received(1).IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct);
	}

	[Fact]
	public async Task CheckAndReserve_BurstExceeded_ThrowsBurst_WithoutTouchingDailyCounter()
	{
		var ct = TestContext.Current.CancellationToken;
		_burst.TryAcquire(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), out Arg.Any<int>())
			.Returns(call => { call[3] = 15; return false; });
		var service = CreateService();

		var ex = (await service.Invoking(s => s.CheckAndReserveAsync(_userId, ct))
			.Should().ThrowAsync<UsageQuotaExceededException>()).Which;

		ex.LimitKind.Should().Be(UsageLimitKind.Burst);
		ex.RetryAfterSeconds.Should().Be(15);
		// Not applicable on a burst rejection — and not read from the DB on the throttle path.
		ex.Remaining.Should().BeNull();
		await _dailyUsage.DidNotReceive().IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task CheckAndReserve_WhenDisabled_SkipsAllChecksAndDoesNotReserve()
	{
		var ct = TestContext.Current.CancellationToken;
		var service = CreateService(new UsageQuotaOptions { Enabled = false, DailyRequestLimit = 1 });

		var status = await service.CheckAndReserveAsync(_userId, ct);

		// Not enforced -> no finite remaining to report.
		status.Remaining.Should().BeNull();
		await _dailyUsage.DidNotReceive().IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
		_burst.DidNotReceive().TryAcquire(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), out Arg.Any<int>());
	}

	[Fact]
	public async Task CheckAndReserve_WithUnlimitedDailyLimit_ReturnsNullRemaining()
	{
		// A daily limit <= 0 means "unlimited" — remaining must be null (not int.MaxValue) so the client never
		// renders something like "2,147,483,647 left".
		var ct = TestContext.Current.CancellationToken;
		AllowBurst();
		_dailyUsage.IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct).Returns(42);
		var service = CreateService(new UsageQuotaOptions { DailyRequestLimit = 0 });

		var status = await service.CheckAndReserveAsync(_userId, ct);

		status.Remaining.Should().BeNull();
		status.Used.Should().Be(42);
	}

	[Fact]
	public async Task CheckAndReserve_PerUserOverride_BeatsAppSettings()
	{
		var ct = TestContext.Current.CancellationToken;
		AllowBurst();
		var userGuid = Guid.Parse(_userId);
		_overrides.GetByUserIdAsync(userGuid, ct).Returns(new UserQuotaOverride(userGuid, dailyRequestLimit: 2));
		_dailyUsage.IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), ct).Returns(3);
		// App default is generous; the override of 2 must be what gets enforced.
		var service = CreateService(new UsageQuotaOptions { DailyRequestLimit = 200 });

		await service.Invoking(s => s.CheckAndReserveAsync(_userId, ct))
			.Should().ThrowAsync<UsageQuotaExceededException>();
	}

	[Fact]
	public async Task RecordTokens_DelegatesToRepository()
	{
		var ct = TestContext.Current.CancellationToken;
		var service = CreateService();

		await service.RecordTokensAsync(_userId, 1200, 340, ct);

		await _dailyUsage.Received(1).AddTokensAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), 1200, 340, ct);
	}

	[Fact]
	public async Task RecordTokens_WithNoTokens_IsNoOp()
	{
		var ct = TestContext.Current.CancellationToken;
		var service = CreateService();

		await service.RecordTokensAsync(_userId, 0, 0, ct);

		await _dailyUsage.DidNotReceive().AddTokensAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetStatus_ReturnsUsedAndRemaining_WithoutReserving()
	{
		var ct = TestContext.Current.CancellationToken;
		var userGuid = Guid.Parse(_userId);
		var service = CreateService(new UsageQuotaOptions { DailyRequestLimit = 50 });

		// A row with a known request count, built via the domain increment method.
		var row = new UserDailyUsage(userGuid, DateOnly.FromDateTime(DateTime.UtcNow));
		row.IncrementRequest();
		row.IncrementRequest();
		_dailyUsage.GetAsync(userGuid, Arg.Any<DateOnly>(), ct).Returns(row);

		var status = await service.GetStatusAsync(_userId, ct);

		status.DailyLimit.Should().Be(50);
		status.Used.Should().Be(2);
		status.Remaining.Should().Be(48);
		await _dailyUsage.DidNotReceive().IncrementRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
	}
}
