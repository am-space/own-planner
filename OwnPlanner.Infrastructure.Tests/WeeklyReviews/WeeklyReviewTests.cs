using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Goals;
using OwnPlanner.Domain.Goals;
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
		var preferences = new WeeklyReviewPreferences
		{
			Enabled = true, TimeZoneId = "Europe/London", WeekStart = 0, ReminderTime = "17:30"
		};
		var review = WeeklyReviewCalendar.Create(preferences, _clock.Now, false);
		review.Status = "deferred"; review.DeferredUntilUtc = _clock.Now.AddHours(2);
		review.Occurrence = 3; review.Delivery = "claimed"; review.Attempts = 2;
		review.RetryAtUtc = _clock.Now.AddMinutes(5); review.OfferedInChat = true;
		await using (var db = await factory.CreateAsync(Ct))
		{
			await db.GetService<IMigrator>().MigrateAsync("20260922082922_SeparateWeeklyReviewPersistenceModels", Ct);
			await db.Database.ExecuteSqlInterpolatedAsync($"""
				INSERT INTO "WeeklyReviewPreferences" ("Id", "Enabled", "TimeZoneId", "WeekStart", "ReminderTime", "Channel")
				VALUES ({preferences.Id}, {preferences.Enabled}, {preferences.TimeZoneId}, {preferences.WeekStart}, {preferences.ReminderTime}, {preferences.Channel})
				""", Ct);
			await db.Database.ExecuteSqlInterpolatedAsync($"""
				INSERT INTO "WeeklyReviews" ("Id", "TargetWeek", "TimeZoneId", "StartsAtUtc", "EndsAtUtc", "ScheduledAtUtc",
					"Status", "DeferredUntilUtc", "Occurrence", "Delivery", "Attempts", "RetryAtUtc", "OfferedInChat")
				VALUES ({review.Id}, {review.TargetWeek}, {review.TimeZoneId}, {review.StartsAtUtc}, {review.EndsAtUtc}, {review.ScheduledAtUtc},
					{review.Status}, {review.DeferredUntilUtc}, {review.Occurrence}, {review.Delivery}, {review.Attempts}, {review.RetryAtUtc}, {review.OfferedInChat})
				""", Ct);
		}
		var store = new WeeklyReviewStore(factory);
		await using (var db = await factory.CreateAsync(Ct))
			await db.Database.MigrateAsync(Ct);
		(await store.GetPreferencesAsync(Ct)).Should().BeEquivalentTo(preferences);
		var persisted = await store.UpdateAsync((_, reviews) => reviews.Single(), Ct);
		persisted.Should().BeEquivalentTo(review);
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
			new TaskListService(new TaskListRepository(_factory)), _clock, new GoalService(new GoalRepository(_factory)));
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

	[Fact]
	public async Task GoalReviewCountsActiveGoalWorkAndOmitsPlannedGoalTasksFromTaskPage()
	{
		await Enable();
		var active = new Goal("Active goal", GoalHorizon.Monthly, targetPeriod: "2026-09");
		var empty = new Goal("Empty goal", GoalHorizon.Yearly, targetPeriod: "2026");
		var achieved = new Goal("Finished goal", GoalHorizon.Yearly, targetPeriod: "2026");
		achieved.SetStatus(GoalStatus.Achieved);
		var list = new TaskList("Tasks");
		var planned = new TaskItem("Goal plan", list.Id, dueAt: _clock.Now.AddDays(-1), goalId: active.Id);
		planned.SetFocusAt(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));
		var completedGoal = new TaskItem("Done for goal", list.Id, goalId: active.Id); completedGoal.Complete();
		var completedOther = new TaskItem("Other done", list.Id, goalId: achieved.Id); completedOther.Complete();
		var unlinked = new TaskItem("Other plan", list.Id);
		unlinked.SetFocusAt(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.AddRange(active, empty, achieved, list, planned, completedGoal, completedOther, unlinked);
			db.Entry(active).Property(g => g.CreatedAt).CurrentValue = _clock.Now.AddDays(-30);
			db.Entry(empty).Property(g => g.CreatedAt).CurrentValue = _clock.Now.AddDays(-30);
			db.Entry(completedGoal).Property(t => t.CompletedAt).CurrentValue = _clock.Now.AddDays(-2);
			db.Entry(completedOther).Property(t => t.CompletedAt).CurrentValue = _clock.Now.AddDays(-2);
			await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Goals.ActiveCount.Should().Be(2);
		view.Report.Goals.Items.First().Title.Should().Be("Empty goal");
		view.Report.Goals.Items.Single(g => g.Id == empty.Id).NoNextStep.Should().BeTrue();
		view.Report.Goals.Items.Single(g => g.Id == active.Id).CompletedLast7.Single().Title.Should().Be("Done for goal");
		view.Report.Goals.CompletedGoalWorkCount.Should().Be(1);
		view.Report.Goals.CompletedOtherWorkCount.Should().Be(1);
		view.Report.Goals.PlannedGoalWorkCount.Should().Be(1);
		view.Report.Goals.PlannedOtherWorkCount.Should().Be(1);
		view.Report.Tasks.Should().NotContain(t => t.Id == planned.Id);
		view.Report.OverdueCount.Should().Be(0);
		view.Report.Goals.UnplannedGoalIds.Should().Contain(empty.Id);
		view.Report.Goals.UnplannedGoalIds.Should().NotContain(active.Id);
		WeeklyReviewCalendar.Notification(view.Review.TargetWeek, view.Report).Should().Contain("2 active goals").And.NotContain("Active goal");
	}

	[Fact]
	public async Task GoalReviewKeepsExactCountsAndTenSamplesForEachGoal()
	{
		await Enable();
		var goals = new[]
		{
			new Goal("First", GoalHorizon.Yearly, targetPeriod: "2026"),
			new Goal("Second", GoalHorizon.Yearly, targetPeriod: "2026")
		};
		var list = new TaskList("Tasks");
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.Add(list);
			db.Goals.AddRange(goals);
			foreach (var goal in goals)
			{
				for (var index = 0; index < 12; index++)
				{
					var completed = new TaskItem($"{goal.Title} completed {index}", list.Id, goalId: goal.Id);
					completed.Complete();
					db.Entry(completed).Property(t => t.CompletedAt).CurrentValue = _clock.Now.AddDays(-1);
					db.Add(completed);
					db.Add(new TaskItem($"{goal.Title} open {index}", list.Id, goalId: goal.Id));
				}
			}
			await db.SaveChangesAsync(Ct);
		}
		var report = (await Service.OpenAsync(ct: Ct)).Report.Goals;
		foreach (var goal in goals)
		{
			var row = report.Items.Single(item => item.Id == goal.Id);
			row.CompletedLast7Count.Should().Be(12);
			row.CompletedLast7.Should().HaveCount(10).And.OnlyContain(task => task.Title.StartsWith(goal.Title));
			row.OpenTaskCount.Should().Be(12);
			row.OpenTasks.Should().HaveCount(10).And.OnlyContain(task => task.Title.StartsWith(goal.Title));
		}
		report.CompletedGoalWorkCount.Should().Be(24);
	}

	[Fact]
	public async Task GoalOnlyWorkTriggersCountsOnlyReminder_AndChatClaimsGoalSuggestionOnce()
	{
		await Enable();
		var first = await Service.OpenAsync(ct: Ct);
		first.SuggestCreatingGoals.Should().BeFalse();
		(await Service.OpenAsync(first.Review.Id, ct: Ct, conversational: true)).SuggestCreatingGoals.Should().BeTrue();
		(await Service.OpenAsync(first.Review.Id, ct: Ct, conversational: true)).SuggestCreatingGoals.Should().BeFalse();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.Goals.Add(new Goal("PRIVATE GOAL", GoalHorizon.Yearly, targetPeriod: "2026"));
			await db.SaveChangesAsync(Ct);
		}
		var claim = await Service.ClaimReminderAsync(Ct);
		claim.Should().NotBeNull();
		claim!.Text.Should().Contain("1 active goal: 1 has no next step; 0 have had no progress")
			.And.NotContain("PRIVATE GOAL");
	}

	[Fact]
	public async Task GoalActionsRequireActiveGoalConfirmationAndFreshTaskRevision()
	{
		await Enable();
		var goal = new Goal("Active", GoalHorizon.Yearly, targetPeriod: "2026");
		var archivedGoal = new Goal("Achieved", GoalHorizon.Yearly, targetPeriod: "2026"); archivedGoal.SetStatus(GoalStatus.Achieved);
		var list = new TaskList("Tasks");
		var goalTask = new TaskItem("Unscheduled step", list.Id, goalId: goal.Id);
		var unlinked = new TaskItem("Unlinked carryover", list.Id, dueAt: _clock.Now.AddDays(-1));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.AddRange(goal, archivedGoal, list, goalTask, unlinked);
			await db.SaveChangesAsync(Ct);
		}
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Tasks.Should().NotContain(t => t.Id == goalTask.Id);
		var actions = new WeeklyReviewActions(_store,
			new TaskItemService(new TaskItemRepository(_factory), new TaskListRepository(_factory)),
			new TaskListService(new TaskListRepository(_factory)), _clock, new GoalService(new GoalRepository(_factory)));
		(await actions.ApplyAsync(view.Review.Id, goalTask.Id, goalTask.UpdatedAt, "goalFocus", new DateOnly(2026, 9, 22), ct: Ct, goalId: archivedGoal.Id)).Applied.Should().BeFalse();
		var focused = await actions.ApplyAsync(view.Review.Id, goalTask.Id, goalTask.UpdatedAt, "goalFocus", new DateOnly(2026, 9, 22), ct: Ct, goalId: goal.Id);
		focused.Applied.Should().BeTrue();
		focused.Task!.FocusAt.Should().Be(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));
		(await actions.ApplyAsync(view.Review.Id, goalTask.Id, goalTask.UpdatedAt, "goalFocus", new DateOnly(2026, 9, 23), ct: Ct, goalId: goal.Id)).Applied.Should().BeFalse();
		var linked = await actions.ApplyAsync(view.Review.Id, unlinked.Id, unlinked.UpdatedAt, "linkGoal", ct: Ct, goalId: goal.Id);
		linked.Applied.Should().BeTrue();
		linked.Task!.GoalId.Should().Be(goal.Id);
	}

	private async Task<Goal> SeedPausedGoal(string title = "Spanish", int daysAgo = 60)
	{
		var goal = new Goal(title, GoalHorizon.Yearly, targetPeriod: "2026");
		goal.SetStatus(GoalStatus.Paused, _clock.Now.AddDays(-daysAgo));
		await using var db = await _factory.CreateAsync(Ct);
		db.Goals.Add(goal);
		await db.SaveChangesAsync(Ct);
		return goal;
	}

	[Fact]
	public async Task MonthlyMentionAndLimitWarningAreClaimedOnceAcrossConcurrentOpensAndRestart()
	{
		await Enable();
		await SeedPausedGoal();
		await SeedPausedGoal("Balcony", 21);
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.Goals.AddRange(Enumerable.Range(0, 6).Select(i => new Goal($"Active {i}", GoalHorizon.Yearly)));
			await db.SaveChangesAsync(Ct);
		}
		var views = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Service.OpenAsync(ct: Ct), Ct)));
		views.Count(v => v.PausedGoalsMention is not null).Should().Be(1);
		views.Single(v => v.PausedGoalsMention is not null).PausedGoalsMention.Should()
			.Contain("2 goals are paused").And.Contain("Spanish\" (paused 2 months ago)")
			.And.Contain("Balcony\" (paused 3 weeks ago)").And.Contain("Resume any of them?");
		views.Count(v => v.ActiveGoalWarning is not null).Should().Be(1);
		views.Single(v => v.ActiveGoalWarning is not null).ActiveGoalWarning.Should().Contain("6 active goals");
		var restarted = new WeeklyReviewService(new WeeklyReviewStore(_factory), _clock);
		var reopened = await restarted.OpenAsync(ct: Ct);
		reopened.PausedGoalsMention.Should().BeNull();
		reopened.ActiveGoalWarning.Should().BeNull();
		_clock.Now = _clock.Now.AddDays(7);
		var next = await restarted.OpenAsync(ct: Ct);
		next.PausedGoalsMention.Should().BeNull();
		next.ActiveGoalWarning.Should().NotBeNull();
	}

	[Theory]
	[InlineData("skip")]
	[InlineData("defer")]
	public async Task SkippingOrDeferringWithoutPresentationDoesNotConsumeMonthlyMention(string action)
	{
		await Enable(); await SeedPausedGoal();
		var lookup = await Service.OpenAsync(ct: Ct, present: false);
		lookup.PausedGoalsMention.Should().BeNull();
		lookup.Review.Status.Should().Be("notStarted");
		await Service.TransitionAsync(lookup.Review.Id, action, action == "defer" ? "2026-09-21T18:00" : null, Ct);
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		if (action == "skip")
		{
			(await Service.OpenAsync(lookup.Review.Id, ct: Ct)).PausedGoalsMention.Should().BeNull();
			_clock.Now = _clock.Now.AddDays(7);
		}
		else _clock.Now = _clock.Now.AddDays(1).AddHours(1);
		(await Service.OpenAsync(ct: Ct)).PausedGoalsMention.Should().NotBeNull();
	}

	[Fact]
	public async Task MonthlyMentionUsesLocalOpeningMonthNotUtcOrTargetWeek_AndDoesNotConsumeEmptyMonth()
	{
		await Enable("America/Los_Angeles");
		_clock.Now = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc); // September locally.
		(await Service.OpenAsync(ct: Ct)).PausedGoalsMention.Should().BeNull();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		await SeedPausedGoal();
		var september = await Service.OpenAsync(ct: Ct);
		september.PausedGoalsMention.Should().NotBeNull();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().Be(new DateOnly(2026, 9, 1));
		_clock.Now = _clock.Now.AddHours(7); // October locally, still same target week.
		var october = await Service.OpenAsync(ct: Ct);
		october.Review.Id.Should().Be(september.Review.Id);
		october.PausedGoalsMention.Should().NotBeNull();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().Be(new DateOnly(2026, 10, 1));
		(await Service.OpenAsync(ct: Ct)).PausedGoalsMention.Should().BeNull();
	}

	[Fact]
	public async Task PausedGoalsAreExcludedFromFlagsAndRemindersButTheirTasksRemainReviewableAndLinked()
	{
		await Enable();
		var paused = await SeedPausedGoal();
		var task = await Seed(dueAt: _clock.Now.AddDays(-1));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			var linked = (await db.TaskItems.FindAsync([task.Id], Ct))!;
			linked.SetGoalId(paused.Id);
			await db.SaveChangesAsync(Ct);
		}
		var claim = await Service.ClaimReminderAsync(Ct);
		claim.Should().NotBeNull();
		claim!.Text.Should().NotContain("Spanish").And.NotContain("paused").And.NotContain("active goal");
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		var view = await Service.OpenAsync(ct: Ct);
		view.Report.Goals.ActiveCount.Should().Be(0);
		view.Report.Goals.NoNextStepCount.Should().Be(0);
		view.Report.Goals.StalledCount.Should().Be(0);
		view.Report.Goals.Items.Should().BeEmpty();
		view.Report.Tasks.Should().ContainSingle(t => t.Id == task.Id && t.GoalId == paused.Id);
		view.PausedGoalsMention.Should().NotBeNull();
		await using var verify = await _factory.CreateAsync(Ct);
		(await verify.TaskItems.FindAsync([task.Id], Ct))!.GoalId.Should().Be(paused.Id);
	}

	[Fact]
	public async Task PagingAndExpiredReviewDoNotConsumeMonthlyMention()
	{
		await Enable(); await SeedPausedGoal();
		var page = await Service.OpenAsync(offset: 20, ct: Ct);
		page.PausedGoalsMention.Should().BeNull();
		(await Service.GetPreferencesAsync(Ct)).PausedGoalsMentionMonth.Should().BeNull();
		_clock.Now = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
		(await Service.OpenAsync(page.Review.Id, ct: Ct)).PausedGoalsMention.Should().BeNull();
		(await Service.OpenAsync(ct: Ct)).PausedGoalsMention.Should().NotBeNull();
	}

	[Fact]
	public async Task ResumedGoalGraceFlowsFromStoredTimestampIntoReport()
	{
		await Enable();
		var goal = await SeedPausedGoal();
		await using (var db = await _factory.CreateAsync(Ct))
		{
			db.Entry((await db.Goals.FindAsync([goal.Id], Ct))!).Property(g => g.CreatedAt).CurrentValue = _clock.Now.AddDays(-90);
			await db.SaveChangesAsync(Ct);
		}
		var service = new GoalService(new GoalRepository(_factory), _clock);
		await service.UpdateAsync(goal.Id, status: GoalStatus.Active, ct: Ct);
		(await Service.OpenAsync(ct: Ct)).Report.Goals.Items.Single().Stalled.Should().BeFalse();
		_clock.Now = _clock.Now.AddDays(13);
		(await Service.OpenAsync(ct: Ct)).Report.Goals.Items.Single().Stalled.Should().BeFalse();
		_clock.Now = _clock.Now.AddDays(1);
		(await Service.OpenAsync(ct: Ct)).Report.Goals.Items.Single().Stalled.Should().BeTrue();
	}

	[Fact]
	public async Task PausedGoalMigrationPreservesEveryExistingStatusAndLeavesTimestampsEmpty()
	{
		var factory = new Factory(Path.Combine(_directory, "old-goals.db"));
		await using var db = await factory.CreateAsync(Ct);
		await db.GetService<IMigrator>().MigrateAsync("20261001083736_TrackWeeklyGoalCreationOffer", Ct);
		foreach (var status in new[] { GoalStatus.Active, GoalStatus.Achieved, GoalStatus.Dropped })
		{
			await db.Database.ExecuteSqlInterpolatedAsync($"""
				INSERT INTO Goals (Id, Title, Horizon, Status, CreatedAt, UpdatedAt)
				VALUES ({Guid.NewGuid()}, {status.ToString()}, {GoalHorizon.Yearly}, {status}, {_clock.Now}, {_clock.Now})
				""", Ct);
		}
		await db.Database.MigrateAsync(Ct);
		var goals = await db.Goals.ToListAsync(Ct);
		goals.Select(g => g.Status).Should().BeEquivalentTo([GoalStatus.Active, GoalStatus.Achieved, GoalStatus.Dropped]);
		goals.Should().OnlyContain(g => g.PausedAt == null && g.LastResumedAt == null);
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
