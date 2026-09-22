using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.WeeklyReviews;

public sealed class WeeklyReminderDispatcherTests
{
	[Fact]
	public async Task SlowDeliveryDoesNotBlockLaterUsers_AndEachCompletionBelongsToItsUser()
	{
		var ct = TestContext.Current.CancellationToken;
		var host = new GatedHost(8, blockEveryUser: false);
		var run = new WeeklyReminderDispatcher(host, NullLogger<WeeklyReminderDispatcher>.Instance).RunOnceAsync(ct);
		try
		{
			await host.FastUsersFinished.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
			host.Completed.Keys.Should().BeEquivalentTo(host.Users.Skip(1));
			host.Started[host.Users[0]].Should().Be(1);
		}
		finally { host.Release.TrySetResult(); }
		await run;
		host.Completed.Keys.Should().BeEquivalentTo(host.Users);
		host.Started.Values.Should().OnlyContain(count => count == 1);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrencyIsBounded_AndCancellationReleasesAllUserScopes(bool cancel)
	{
		var ct = TestContext.Current.CancellationToken;
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var host = new GatedHost(12, blockEveryUser: true);
		var run = new WeeklyReminderDispatcher(host, NullLogger<WeeklyReminderDispatcher>.Instance).RunOnceAsync(stop.Token);
		try
		{
			await host.FourUsersStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
			host.Started.Should().HaveCount(4);
			host.Active.Should().Be(4);
			if (cancel) await stop.CancelAsync();
			else host.Release.TrySetResult();
			if (cancel)
			{
				var canceled = async () => await run;
				await canceled.Should().ThrowAsync<OperationCanceledException>();
				host.Completed.Should().BeEmpty();
			}
			else
			{
				await run;
				host.Completed.Should().HaveCount(12);
			}
			host.Active.Should().Be(0);
			host.MaximumActive.Should().Be(4);
		}
		finally
		{
			host.Release.TrySetResult();
			await stop.CancelAsync();
			try { await run; } catch (OperationCanceledException) { }
		}
	}

	private sealed class GatedHost(int count, bool blockEveryUser) : IWeeklyReminderHost
	{
		private int _active;
		private int _maximumActive;
		public Guid[] Users { get; } = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
		public ConcurrentDictionary<Guid, int> Started { get; } = new();
		public ConcurrentDictionary<Guid, bool> Completed { get; } = new();
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource FourUsersStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource FastUsersFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public int Active => Volatile.Read(ref _active);
		public int MaximumActive => Volatile.Read(ref _maximumActive);

		public Task<IReadOnlyList<Guid>> GetLinkedUsersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(Users);

		public async Task WithUserAsync(Guid userId, Func<IWeeklyReviewService, Func<string, CancellationToken, Task>, Task> work, CancellationToken ct)
		{
			Started.AddOrUpdate(userId, 1, (_, existing) => existing + 1);
			var active = Interlocked.Increment(ref _active);
			int previous;
			do { previous = _maximumActive; }
			while (active > previous && Interlocked.CompareExchange(ref _maximumActive, active, previous) != previous);
			var service = Substitute.For<IWeeklyReviewService>();
			var claim = new WeeklyReminderClaim(userId, 0, userId.ToString());
			service.ClaimReminderAsync(ct).Returns(claim);
			service.FinishDeliveryAsync(claim, true, ct: ct).Returns(_ =>
			{
				Completed.TryAdd(userId, true);
				if (Users.Skip(1).All(Completed.ContainsKey)) FastUsersFinished.TrySetResult();
				return Task.CompletedTask;
			});
			try
			{
				await work(service, async (text, token) =>
				{
					text.Should().Be(userId.ToString());
					token.Should().Be(ct);
					if (active == 4) FourUsersStarted.TrySetResult();
					if (blockEveryUser || userId == Users[0]) await Release.Task.WaitAsync(token);
				});
			}
			finally { Interlocked.Decrement(ref _active); }
		}
	}
}
