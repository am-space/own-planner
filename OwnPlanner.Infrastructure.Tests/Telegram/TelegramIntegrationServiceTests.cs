using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OwnPlanner.Application.Chat;
using OwnPlanner.Application.Telegram;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Telegram;

namespace OwnPlanner.Infrastructure.Tests.Telegram;

public sealed class TelegramIntegrationServiceTests
{
	private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public async Task ConnectionToken_IsHashedSingleUse_AndCreatesDefaultModeLink()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		var link = await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		var token = new Uri(link.Url).Query[7..];

		(await fixture.Db.TelegramConnectionTokens.SingleAsync(ct)).TokenHash.Should().NotContain(token);
		(await fixture.Service.ConsumeConnectionTokenAsync(token, 101, 201, ct)).Should().Be(TelegramLinkResult.Linked);
		(await fixture.Service.ConsumeConnectionTokenAsync(token, 102, 202, ct)).Should().Be(TelegramLinkResult.InvalidOrExpired);

		var account = await fixture.Service.FindLinkedAccountAsync(101, 201, ct);
		account.Should().NotBeNull();
		account!.UserId.Should().Be(fixture.UserA);
		account.Mode.Should().Be(PlanningMode.DayWork);
		(await fixture.Service.GetStatusAsync(fixture.UserA, ct)).Mode.Should().Be("DayWork");
	}

	[Fact]
	public async Task ExpiredToken_IsRejected()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		var link = await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		fixture.Clock.Advance(TimeSpan.FromMinutes(16));

		(await fixture.Service.ConsumeConnectionTokenAsync(new Uri(link.Url).Query[7..], 101, 201, ct))
			.Should().Be(TelegramLinkResult.InvalidOrExpired);
	}

	[Fact]
	public async Task OneTelegramIdentity_CannotLinkTwoUsers()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		var first = await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		var second = await fixture.Service.CreateConnectionLinkAsync(fixture.UserB, ct);
		await fixture.Service.ConsumeConnectionTokenAsync(new Uri(first.Url).Query[7..], 101, 201, ct);

		(await fixture.Service.ConsumeConnectionTokenAsync(new Uri(second.Url).Query[7..], 101, 201, ct))
			.Should().Be(TelegramLinkResult.TelegramAccountAlreadyLinked);
	}

	[Fact]
	public async Task IdentityLookup_RequiresBothPersistedTelegramIds_AndUserDeletionCascadesMetadata()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		var link = await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		await fixture.Service.ConsumeConnectionTokenAsync(new Uri(link.Url).Query[7..], 101, 201, ct);

		(await fixture.Service.FindLinkedAccountAsync(101, 999, ct)).Should().BeNull();
		var user = await fixture.Db.Users.SingleAsync(x => x.Id == fixture.UserA, ct);
		fixture.Db.Users.Remove(user);
		await fixture.Db.SaveChangesAsync(ct);

		(await fixture.Db.TelegramAccountLinks.CountAsync(ct)).Should().Be(0);
		(await fixture.Db.TelegramConnectionTokens.CountAsync(ct)).Should().Be(0);
	}

	[Fact]
	public async Task Disconnect_RemovesLinkAndOutstandingTokens()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		await fixture.Service.DisconnectAsync(fixture.UserA, ct);

		(await fixture.Service.GetStatusAsync(fixture.UserA, ct)).Should().Match<TelegramConnectionStatus>(x => !x.Connected && !x.Pending);
		(await fixture.Db.TelegramConnectionTokens.CountAsync(ct)).Should().Be(0);
	}

	[Fact]
	public async Task ReserveUpdate_DeduplicatesUpdateIdAndRetainsFailureStatus()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		(await fixture.Service.ReserveUpdateAsync(42, ct)).Should().Be(TelegramUpdateReservation.Reserved);
		await fixture.Service.CompleteUpdateAsync(42, false, ct);
		(await fixture.Service.ReserveUpdateAsync(42, ct)).Should().Be(TelegramUpdateReservation.Duplicate);
		(await fixture.Db.TelegramProcessedUpdates.SingleAsync(ct)).Succeeded.Should().BeFalse();
	}

	[Fact]
	public async Task ReserveUpdate_PrunesRowsOutsideConfiguredRetryWindow()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		fixture.Db.TelegramProcessedUpdates.AddRange(
			new TelegramProcessedUpdate { UpdateId = 1, ReservedAtUtc = Now.UtcDateTime.AddDays(-8) },
			new TelegramProcessedUpdate { UpdateId = 2, ReservedAtUtc = Now.UtcDateTime.AddDays(-6) });
		await fixture.Db.SaveChangesAsync(ct);

		(await fixture.Service.ReserveUpdateAsync(3, ct)).Should().Be(TelegramUpdateReservation.Reserved);

		(await fixture.Db.TelegramProcessedUpdates.AsNoTracking().Select(x => x.UpdateId).ToListAsync(ct))
			.Should().BeEquivalentTo([2L, 3L]);
	}

	[Fact]
	public async Task ChatUpdateHighWater_AllowsIncreasingIdsAndRejectsStaleIds()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var fixture = await Fixture.CreateAsync(ct);
		var link = await fixture.Service.CreateConnectionLinkAsync(fixture.UserA, ct);
		await fixture.Service.ConsumeConnectionTokenAsync(new Uri(link.Url).Query[7..], 101, 201, ct);

		(await fixture.Service.TryAdvanceChatUpdateAsync(fixture.UserA, 100, ct)).Should().BeTrue();
		(await fixture.Service.TryAdvanceChatUpdateAsync(fixture.UserA, 99, ct)).Should().BeFalse();
		(await fixture.Service.TryAdvanceChatUpdateAsync(fixture.UserA, 100, ct)).Should().BeFalse();
		(await fixture.Service.TryAdvanceChatUpdateAsync(fixture.UserA, 101, ct)).Should().BeTrue();
	}

	[Fact]
	public void UniqueConstraintDetection_DoesNotMisclassifyOtherDatabaseFailures()
	{
		TelegramIntegrationService.IsUniqueConstraintViolation(
			new DbUpdateException("unique", new SqliteException("constraint", 19))).Should().BeTrue();
		TelegramIntegrationService.IsUniqueConstraintViolation(
			new DbUpdateException("locked", new SqliteException("busy", 5))).Should().BeFalse();
		TelegramIntegrationService.IsUniqueConstraintViolation(new DbUpdateException("disk failure")).Should().BeFalse();
	}

	[Fact]
	public void Split_DoesNotBreakSurrogatePairs_AndKeepsTelegramLimit()
	{
		var text = new string('a', 4095) + "😀" + new string('b', 4096);
		var parts = TelegramBotClient.Split(text, 4096);

		parts.Should().OnlyContain(x => x.Length <= 4096);
		string.Concat(parts).Should().Be(text);
		parts.Should().OnlyContain(x => !char.IsHighSurrogate(x[x.Length - 1]));
	}

	private sealed class Fixture : IAsyncDisposable
	{
		private readonly SqliteConnection _connection;
		public AuthDbContext Db { get; }
		public MutableTimeProvider Clock { get; }
		public TelegramIntegrationService Service { get; }
		public Guid UserA { get; }
		public Guid UserB { get; }

		private Fixture(SqliteConnection connection, AuthDbContext db, MutableTimeProvider clock, Guid userA, Guid userB)
		{
			_connection = connection; Db = db; Clock = clock; UserA = userA; UserB = userB;
			Service = new TelegramIntegrationService(db, Options.Create(new TelegramOptions
			{
				Enabled = true, BotUsername = "ownplanner_test_bot", BotToken = "not-used", WebhookSecret = "secret",
			}), clock);
		}

		public static async Task<Fixture> CreateAsync(CancellationToken cancellationToken)
		{
			var connection = new SqliteConnection("DataSource=:memory:");
			await connection.OpenAsync(cancellationToken);
			var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options);
			await db.Database.EnsureCreatedAsync(cancellationToken);
			var a = new User("a@example.com", "user-a", "hash");
			var b = new User("b@example.com", "user-b", "hash");
			db.Users.AddRange(a, b); await db.SaveChangesAsync(cancellationToken);
			return new Fixture(connection, db, new MutableTimeProvider(Now), a.Id, b.Id);
		}

		public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
	}

	private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
	{
		private DateTimeOffset _now = now;
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan duration) => _now += duration;
	}
}
