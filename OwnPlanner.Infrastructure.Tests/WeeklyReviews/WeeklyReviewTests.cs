using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.WeeklyReviews;

public sealed class WeeklyReviewTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "weekly-review-" + Guid.NewGuid().ToString("N"));
	private readonly Clock _clock = new(new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc));
	private Factory _factory = null!;
	private WeeklyReviewStore _store = null!;
	private WeeklyReviewService Service => new(_store, _clock);
	private CancellationToken Ct => TestContext.Current.CancellationToken;
	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory);
		_factory = new(Path.Combine(_directory, "user.db"));
		_store = new(_factory);
		await using var db = await _factory.CreateAsync(Ct);
		await db.Database.MigrateAsync(Ct);
	}
	public ValueTask DisposeAsync()
	{
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		Directory.Delete(_directory, true);
		return ValueTask.CompletedTask;
	}
	private async Task<TaskItem> Seed(string title = "PRIVATE TITLE", DateTime? dueAt = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		var list = new TaskList("List");
		var task = new TaskItem(title, list.Id, "PRIVATE DESCRIPTION", dueAt);
		task.SetFocusAt(_clock.Now.Date);
		db.AddRange(list, task);
		await db.SaveChangesAsync(Ct);
		return task;
	}
	private Task Enable(string zone = "UTC", int weekStart = 1) => Service.ConfigureAsync(true, zone, weekStart, "18:00", ct: Ct);

	[Fact]
	public async Task ReadingDefaultPreferencesDoesNotPersistRows_AndSnapshotsAreDetached()
	{
		var defaults = await Service.GetPreferencesAsync(Ct);
		defaults.Enabled.Should().BeFalse();
		defaults.Id.Should().Be(1);
		await using (var db = await _factory.CreateAsync(Ct))
		{
			(await db.WeeklyReviewPreferences.CountAsync(Ct)).Should().Be(0);
			(await db.WeeklyReviews.CountAsync(Ct)).Should().Be(0);
		}
		await Enable("Europe/London", 0);
		var snapshot = await Service.GetPreferencesAsync(Ct);
		snapshot.TimeZoneId = "UTC";
		(await Service.GetPreferencesAsync(Ct)).TimeZoneId.Should().Be("Europe/London");
	}

	[Fact]
	public async Task PersistenceModelMigrationPreservesExistingReviewAndPreferenceData()
	{
		var factory = new Factory(Path.Combine(_directory, "upgrade.db"));
		await using (var db = await factory.CreateAsync(Ct))
			await db.GetService<IMigrator>().MigrateAsync("20260921164009_AddWeeklyReviews", Ct);
		var store = new WeeklyReviewStore(factory);
		var original = await store.UpdateAsync((preferences, reviews) =>
		{
			preferences.Enabled = true; preferences.TimeZoneId = "Europe/London";
			preferences.WeekStart = 0; preferences.ReminderTime = "17:30";
			var review = WeeklyReviewCalendar.Create(preferences, _clock.Now, false);
			review.Status = "deferred"; review.DeferredUntilUtc = _clock.Now.AddHours(2);
			review.Occurrence = 3; review.Delivery = "claimed"; review.Attempts = 2;
			review.RetryAtUtc = _clock.Now.AddMinutes(5); review.OfferedInChat = true;
			reviews.Add(review);
			return (preferences, review);
		}, Ct);
		await using (var db = await factory.CreateAsync(Ct))
			await db.Database.MigrateAsync(Ct);
		(await store.GetPreferencesAsync(Ct)).Should().BeEquivalentTo(original.preferences);
		var persisted = await store.UpdateAsync((_, reviews) => reviews.Single(), Ct);
		persisted.Should().BeEquivalentTo(original.review);
		// Persistence types never become public results, and no Domain audit fields appear on the wire.
		JsonSerializer.SerializeToElement(persisted).EnumerateObject().Select(p => p.Name)
			.Should().NotContain(["CreatedAt", "UpdatedAt"]);
	}

	[Fact]
	public async Task OptInEmptySelectionAndMissingTimezone_AreSafe()
	{
		(await Service.GetPreferencesAsync(Ct)).Enabled.Should().BeFalse();
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		var open = () => Service.OpenAsync(ct: Ct);
		await open.Should().ThrowAsync<ArgumentException>();
		await Enable();
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		_clock.Now = _clock.Now.AddDays(1);
		(await Service.OfferAsync(Ct)).Should().BeNull();
	}

	[Fact]
	public async Task Report_IsBoundedDeduplicatedFreshAndDoesNotReadDescriptions()
	{
		await Enable("America/Los_Angeles");
		_clock.Now = new DateTime(2026, 9, 21, 1, 0, 0, DateTimeKind.Utc); // Sunday locally.
		var both = await Seed(dueAt: _clock.Now.AddHours(-1));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			// Sunday focus is a calendar date, not an instant shifted into Saturday.
			var task = await db.TaskItems.FindAsync([both.Id], Ct); task!.SetFocusAt(new DateTime(2026, 9, 20));
			var targetStart = new DateTime(2026, 9, 21, 7, 0, 0, DateTimeKind.Utc);
			var atStart = new TaskItem("Boundary included", both.TaskListId, dueAt: targetStart);
			var atEnd = new TaskItem("Boundary excluded", both.TaskListId, dueAt: targetStart.AddDays(7));
			var completed = new TaskItem("Completed excluded", both.TaskListId, dueAt: _clock.Now.AddDays(-1)); completed.Complete();
			var trash = new TaskItem("Trash excluded", both.TaskListId, dueAt: _clock.Now.AddDays(-1)); trash.Trash();
			var archivedList = new TaskList("Archived"); archivedList.Archive();
			db.AddRange(atStart, atEnd, completed, trash, archivedList, new TaskItem("Archive excluded", archivedList.Id, dueAt: _clock.Now.AddDays(-1)));
			await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(limit: 1, ct: Ct);
		view.Report.TotalCount.Should().Be(2);
		view.Report.CarryoverCount.Should().Be(1);
		view.Report.OverdueCount.Should().Be(1);
		view.Report.DueInTargetWeekCount.Should().Be(1);
		view.Report.Tasks.Should().ContainSingle(t => t.Id == both.Id && t.Carryover && t.Overdue);
		var page = await Service.OpenAsync(view.Review.Id, 1, 1, Ct);
		page.Report.Tasks.Should().ContainSingle(t => t.Title == "Boundary included");
		JsonSerializer.Serialize(view).Should().NotContain("PRIVATE DESCRIPTION");
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var task = await db.TaskItems.FindAsync([both.Id], Ct); task!.Complete(); await db.SaveChangesAsync(Ct);
		}
		(await Service.OpenAsync(view.Review.Id, ct: Ct)).Report.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task DeliveryIsClaimedOnceAcrossConcurrentWorkersAndRestarts_AndDoesNotCompleteReview()
	{
		await Enable(); await Seed();
		var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Service.ClaimReminderAsync(Ct), Ct)));
		claims.OfType<WeeklyReminderClaim>().Should().ContainSingle();
		var claim = claims.OfType<WeeklyReminderClaim>().Single();
		claim.Text.Should().NotContain("PRIVATE");
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		await Service.FinishDeliveryAsync(claim, true, ct: Ct);
		_clock.Now = _clock.Now.AddDays(1);
		(await Service.OfferAsync(Ct)).Should().BeNull();
		var opened = await Service.OpenAsync(ct: Ct);
		opened.Review.Id.Should().Be(claim.ReviewId);
		opened.Review.Status.Should().Be("inProgress");
		await Service.TransitionAsync(claim.ReviewId, "complete", ct: Ct);
		(await Service.OfferAsync(Ct)).Should().BeNull();
	}

	[Fact]
	public async Task MissedDeliveryHasOneSharedFallback_AndDisabledOrSkippedReviewsAreSuppressed()
	{
		await Enable(); await Seed();
		var sunday = await Service.OpenAsync(ct: Ct);
		_clock.Now = _clock.Now.AddDays(1);
		(await Service.OpenAsync(ct: Ct)).Review.Id.Should().Be(sunday.Review.Id);
		var offers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Service.OfferAsync(Ct), Ct)));
		offers.OfType<string>().Should().ContainSingle();
		await Service.TransitionAsync(sunday.Review.Id, "skip", ct: Ct);
		(await Service.OfferAsync(Ct)).Should().BeNull();
		_clock.Now = _clock.Now.AddDays(7);
		await Service.ConfigureAsync(false, "UTC", 1, "18:00", ct: Ct);
		(await Service.OfferAsync(Ct)).Should().BeNull();
	}

	[Fact]
	public async Task DeferralRetainsIdentityCreatesOneOccurrenceAndExpiresWithoutReplay()
	{
		await Enable(); await Seed();
		var claim = (await Service.ClaimReminderAsync(Ct))!;
		await Service.FinishDeliveryAsync(claim, true, ct: Ct);
		var deferred = await Service.TransitionAsync(claim.ReviewId, "defer", "2026-09-21T18:00", Ct);
		deferred.TargetWeek.Should().Be(new DateOnly(2026, 9, 21));
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		(await Service.OfferAsync(Ct)).Should().BeNull();
		_clock.Now = _clock.Now.AddDays(1);
		var next = (await Service.ClaimReminderAsync(Ct))!;
		next.ReviewId.Should().Be(claim.ReviewId);
		next.Occurrence.Should().Be(1);
		await Service.FinishDeliveryAsync(claim, true, ct: Ct); // Stale completion cannot mark the new occurrence.
		(await Service.OpenAsync(claim.ReviewId, ct: Ct)).Review.Delivery.Should().Be("claimed");
		await Service.FinishDeliveryAsync(next, false, ct: Ct);
		(await Service.OfferAsync(Ct)).Should().NotBeNull();
		_clock.Now = _clock.Now.AddDays(7);
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		(await Service.OpenAsync(ct: Ct)).Review.Id.Should().NotBe(claim.ReviewId);
	}

	[Theory]
	[InlineData("2026-09-20T17:00")]
	[InlineData("2026-09-28T00:00")]
	[InlineData("tomorrow")]
	public async Task InvalidDeferralsDoNotChangePersistedState(string localTime)
	{
		await Enable(); await Seed();
		var view = await Service.OpenAsync(ct: Ct);
		var action = () => Service.TransitionAsync(view.Review.Id, "defer", localTime, Ct);
		await action.Should().ThrowAsync<ArgumentException>();
		var saved = await Service.OpenAsync(view.Review.Id, ct: Ct);
		saved.Review.Status.Should().Be("inProgress");
		saved.Review.Occurrence.Should().Be(0);
	}

	[Fact]
	public async Task CrashedClaimIsNeverReplayed_AndNewWeekFallbackRemainsAvailable()
	{
		await Enable(); await Seed();
		var claim = (await Service.ClaimReminderAsync(Ct))!;
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		_clock.Now = _clock.Now.AddDays(1);
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		(await Service.OfferAsync(Ct)).Should().NotBeNull();
		(await Service.OpenAsync(ct: Ct)).Review.Id.Should().Be(claim.ReviewId);
	}

	[Fact]
	public async Task CalendarPreferenceChangesReuseOverlappingReviewAndSuppressFinishedPrompt()
	{
		await Enable(); await Seed();
		var first = await Service.OpenAsync(ct: Ct);
		await Service.TransitionAsync(first.Review.Id, "skip", ct: Ct);
		await Enable("America/Los_Angeles", 0);
		var edited = await Service.OpenAsync(ct: Ct);
		edited.Review.Id.Should().Be(first.Review.Id);
		edited.Review.TimeZoneId.Should().Be("UTC");
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
	}

	[Fact]
	public async Task ExistingScheduleRemainsFrozenAfterTimeEdits_AndDeliveredDeferralDoesNotBlockNextWeek()
	{
		await Enable(); await Seed();
		var current = await Service.OpenAsync(ct: Ct);
		await Service.ConfigureAsync(true, "UTC", 1, "23:00", ct: Ct);
		var claim = (await Service.ClaimReminderAsync(Ct))!;
		claim.ReviewId.Should().Be(current.Review.Id);
		await Service.FinishDeliveryAsync(claim, true, ct: Ct);
		await Service.TransitionAsync(claim.ReviewId, "defer", "2026-09-21T18:00", Ct);
		_clock.Now = _clock.Now.AddDays(1);
		var deferred = (await Service.ClaimReminderAsync(Ct))!;
		await Service.FinishDeliveryAsync(deferred, true, ct: Ct);
		_clock.Now = new DateTime(2026, 9, 27, 23, 0, 0, DateTimeKind.Utc);
		var nextWeek = (await Service.ClaimReminderAsync(Ct))!;
		nextWeek.Should().NotBeNull();
		nextWeek.ReviewId.Should().NotBe(claim.ReviewId);
		(await Service.OpenAsync(ct: Ct, targetWeek: new DateOnly(2026, 9, 28))).Review.Id.Should().Be(nextWeek.ReviewId);
		// Opening a due deferred review resumes its original target even on the last day.
		(await Service.OpenAsync(ct: Ct)).Review.Id.Should().Be(claim.ReviewId);
	}

	[Fact]
	public async Task AmbiguousOutcomeIsNotRetried_KnownRejectionHasThreeAttemptLimit()
	{
		await Enable(); await Seed();
		var claim = (await Service.ClaimReminderAsync(Ct))!;
		await Service.FinishDeliveryAsync(claim, false, true, Ct);
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		for (var attempt = 2; attempt <= 3; attempt++)
		{
			_clock.Now = _clock.Now.AddMinutes(15);
			claim = (await Service.ClaimReminderAsync(Ct))!;
			claim.Should().NotBeNull();
			await Service.FinishDeliveryAsync(claim, false, true, Ct);
		}
		_clock.Now = _clock.Now.AddMinutes(15);
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		await Service.TransitionAsync(claim.ReviewId, "defer", "2026-09-20T20:00", Ct);
		_clock.Now = new DateTime(2026, 9, 20, 20, 0, 0, DateTimeKind.Utc);
		claim = (await Service.ClaimReminderAsync(Ct))!;
		await Service.FinishDeliveryAsync(claim, false, ct: Ct);
		(await Service.ClaimReminderAsync(Ct)).Should().BeNull();
		(await Service.OfferAsync(Ct)).Should().NotBeNull();
	}

	[Fact]
	public async Task ActionsRecheckChangedCompletedTrashedArchivedTargets_AndReuseDeadlineClearing()
	{
		await Enable();
		var task = await Seed(dueAt: _clock.Now.AddDays(-1));
		var view = await Service.OpenAsync(ct: Ct);
		var actions = new WeeklyReviewActions(_store, new TaskItemService(new TaskItemRepository(_factory), new TaskListRepository(_factory)),
			new TaskListService(new TaskListRepository(_factory)), _clock);
		var revision = view.Report.Tasks.Single().Revision;
		(await actions.ApplyAsync(view.Review.Id, task.Id, revision.AddTicks(-1), "complete", ct: Ct)).Applied.Should().BeFalse();
		var cleared = await actions.ApplyAsync(view.Review.Id, task.Id, revision, "clearDeadline", ct: Ct);
		cleared.Applied.Should().BeTrue(); cleared.Task!.DueAt.Should().BeNull(); cleared.Task.FocusAt.Should().Be(task.FocusAt);
		var focused = await actions.ApplyAsync(view.Review.Id, task.Id, cleared.Task.UpdatedAt, "focus", new DateOnly(2026, 9, 22), ct: Ct);
		focused.Applied.Should().BeTrue(); focused.Task!.DueAt.Should().BeNull();
		var other = await Seed();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var changed = await db.TaskItems.FindAsync([other.Id], Ct); changed!.Complete(); await db.SaveChangesAsync(Ct);
		}
		(await actions.ApplyAsync(view.Review.Id, other.Id, other.UpdatedAt, "trash", ct: Ct)).Applied.Should().BeFalse();
		var trashed = await Seed();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var changed = await db.TaskItems.FindAsync([trashed.Id], Ct); changed!.Trash(); await db.SaveChangesAsync(Ct);
		}
		(await actions.ApplyAsync(view.Review.Id, trashed.Id, trashed.UpdatedAt, "complete", ct: Ct)).Applied.Should().BeFalse();
		var archived = await Seed();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var changed = await db.TaskLists.FindAsync([archived.TaskListId], Ct); changed!.Archive(); await db.SaveChangesAsync(Ct);
		}
		(await actions.ApplyAsync(view.Review.Id, archived.Id, archived.UpdatedAt, "complete", ct: Ct)).Applied.Should().BeFalse();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DispatcherUsesClaimedCountsOnly_AndBoundsFailureRetries(bool rateLimited)
	{
		await Enable(); await Seed();
		var host = new FakeHost(Service, rateLimited);
		var dispatcher = new WeeklyReminderDispatcher(host, NullLogger<WeeklyReminderDispatcher>.Instance);
		await dispatcher.RunOnceAsync(Ct);
		await dispatcher.RunOnceAsync(Ct);
		host.Sends.Should().Be(1);
		host.Text.Should().NotContain("PRIVATE");
		_clock.Now = _clock.Now.AddMinutes(15);
		await dispatcher.RunOnceAsync(Ct);
		host.Sends.Should().Be(rateLimited ? 2 : 1);
	}

	private sealed class Clock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
	private sealed class Factory(string path) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken ct = default) => ValueTask.FromResult(new AppDbContext(
			new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
	}
	private sealed class FakeHost(IWeeklyReviewService service, bool rateLimited) : IWeeklyReminderHost
	{
		public int Sends { get; private set; }
		public string Text { get; private set; } = "";
		public Task<IReadOnlyList<Guid>> GetLinkedUsersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Guid.Empty]);
		public Task WithUserAsync(Guid userId, Func<IWeeklyReviewService, Func<string, CancellationToken, Task>, Task> work, CancellationToken ct) =>
			work(service, (text, _) =>
			{
				Sends++; Text = text;
				throw new HttpRequestException("Simulated", null, rateLimited ? HttpStatusCode.TooManyRequests : null);
			});
	}
}
