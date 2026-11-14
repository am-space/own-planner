using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public sealed partial class RecurringTaskSuccessorTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ownplanner-successor-{Guid.NewGuid():N}");
	private readonly Clock _clock = new(new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc));
	private Factory _factory = null!;
	private CancellationToken Ct => TestContext.Current.CancellationToken;
	private RecurringTaskSuccessors Successors => new(new RecurringTaskStore(_factory));
	private RecurringTaskLifecycle Lifecycle => new(new RecurringTaskStore(_factory),
		new PlanningCalendar(new WeeklyReviewStore(_factory), _clock), new RecurrenceCalculator());
	private TaskItemRepository Repository => new(_factory);
	private static RecurringSuccessorRequest Change(DateOnly? start = null) => new(RecurringSuccessorKind.Replace,
		start ?? new(2026, 11, 16), RecurrenceRule.Weekly(1, [DayOfWeek.Monday], DayOfWeek.Monday));

	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory);
		_factory = new(Path.Combine(_directory, "planner.db"));
		await using var db = await _factory.CreateAsync(Ct);
		await db.Database.MigrateAsync(Ct);
	}
	public ValueTask DisposeAsync() { Directory.Delete(_directory, true); return ValueTask.CompletedTask; }

	private async Task<TaskItem> Seed(MissedOccurrencePolicy policy = MissedOccurrencePolicy.RetainEachObligation,
		DateOnly? start = null, DateOnly? end = null, RecurrenceRule? rule = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		var list = new TaskList("Private list");
		var task = new TaskItem("Training", list.Id, "Details", _clock.Now.AddDays(4), true, Guid.NewGuid());
		task.SetFocusAt(_clock.Now);
		task.ConfigureRecurrence(new(rule ?? RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			start ?? new(2026, 11, 13), end, policy));
		db.AddRange(list, task); await db.SaveChangesAsync(Ct); return task;
	}
	private async Task<TaskItem> Get(Guid id) => (await Repository.GetAsync(id, Ct))!;
	private async Task<TaskOccurrenceFact[]> Facts()
	{
		await using var db = await _factory.CreateAsync(Ct);
		return (await db.TaskOccurrenceFacts.AsNoTracking().OrderBy(f => f.TaskId).ThenBy(f => f.OccurrenceDate).ToArrayAsync(Ct))
			.Select(f => f.ToFact()).ToArray();
	}
	private async Task<RecurringSuccessorResult> Apply(Guid id, RecurringSuccessorRequest request,
		SuccessorConflictResolution? choice = null)
	{
		var preview = await Successors.PreviewAsync(id, request, Ct);
		return await Successors.ApplyAsync(id, Guid.NewGuid(), request, preview.PreviewToken, choice, Ct);
	}

	[Theory]
	[InlineData(MissedOccurrencePolicy.RetainEachObligation)]
	[InlineData(MissedOccurrencePolicy.SkipExpiredPeriods)]
	public async Task FridayRemainsActionableAndExpiresOnlyAtItsOriginalWeekBoundary(MissedOccurrencePolicy policy)
	{
		var source = await Seed(policy);
		var result = await Apply(source.Id, Change());
		var old = await Get(source.Id); var next = await Get(result.SuccessorTaskId);
		old.IsCompleted.Should().BeFalse(); old.Recurrence!.EndDate.Should().Be(new DateOnly(2026, 11, 15));
		old.Recurrence.Rule.Should().Be(source.Recurrence!.Rule); old.Recurrence.StartDate.Should().Be(source.Recurrence.StartDate);
		next.Recurrence!.StartDate.Should().Be(new DateOnly(2026, 11, 16)); next.Recurrence.MissedPolicy.Should().Be(policy);
		next.Title.Should().Be(source.Title); next.Description.Should().Be(source.Description);
		next.TaskListId.Should().Be(source.TaskListId); next.GoalId.Should().Be(source.GoalId);
		next.IsImportant.Should().BeTrue(); next.DueAt.Should().BeNull(); next.FocusAt.Should().BeNull();
		await Lifecycle.ReconcileAsync(Ct); (await Facts()).Should().BeEmpty();
		(await Get(source.Id)).IsCompleted.Should().BeFalse(); // Sunday is still part of Friday's full week
		_clock.Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
		await Lifecycle.ReconcileAsync(Ct);
		(await Get(source.Id)).IsCompleted.Should().Be(policy == MissedOccurrencePolicy.SkipExpiredPeriods);
		var completed = await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 13)), ct: Ct);
		completed.Id.TaskId.Should().Be(source.Id); completed.CompletedAt.Should().Be(_clock.Now);
		await Lifecycle.ReconcileAsync(Ct); (await Get(source.Id)).IsCompleted.Should().BeTrue();
		(await Get(next.Id)).IsCompleted.Should().BeFalse();
		(await Facts()).Should().ContainSingle().Which.Id.TaskId.Should().Be(source.Id);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MidPeriodCutoffDoesNotPrematurelySkipOrCloseThePredecessor(bool monthly)
	{
		var source = await Seed(MissedOccurrencePolicy.SkipExpiredPeriods,
			rule: monthly ? RecurrenceRule.Monthly(1, [13]) : null);
		await Apply(source.Id, Change(new(2026, 11, 14)));
		await Lifecycle.ReconcileAsync(Ct);
		(await Get(source.Id)).Recurrence!.EndDate.Should().Be(new DateOnly(2026, 11, 13));
		(await Get(source.Id)).IsCompleted.Should().BeFalse(); (await Facts()).Should().BeEmpty();
		_clock.Now = monthly ? new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) : new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
		await Lifecycle.ReconcileAsync(Ct);
		(await Get(source.Id)).IsCompleted.Should().BeTrue();
		(await Facts()).Where(f => f.Id.TaskId == source.Id).Should().ContainSingle()
			.Which.Status.Should().Be(TaskOccurrenceStatus.Skipped);
	}

	[Theory]
	[InlineData(SuccessorConflictResolution.MoveStartAfterFacts)]
	[InlineData(SuccessorConflictResolution.KeepStartAsSeparateWork)]
	public async Task BothChoicesPreserveOriginalCompletionAndCancellationFactsWithoutTransferringWork(SuccessorConflictResolution choice)
	{
		var source = await Seed();
		var completedAt = _clock.Now.AddHours(-2);
		await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 20)), completedAt, Ct);
		await Lifecycle.CancelOccurrenceAsync(new(source.Id, new(2026, 11, 27)), Ct);
		var originalFacts = await Facts(); var request = Change();
		var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		preview.AffectedFacts.Should().Equal(originalFacts);
		preview.ProposedSchedule.StartDate.Should().Be(new DateOnly(2026, 11, 16));
		preview.LaterSchedule!.StartDate.Should().Be(new DateOnly(2026, 11, 28));
		preview.PredecessorSchedule.EndDate.Should().Be(new DateOnly(2026, 11, 15));
		preview.LaterPredecessorSchedule!.EndDate.Should().Be(new DateOnly(2026, 11, 27));
		var unspecified = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, ct: Ct);
		await unspecified.Should().ThrowAsync<InvalidOperationException>().WithMessage("*separate work*");
		(await Get(source.Id)).Should().BeEquivalentTo(source); (await Facts()).Should().Equal(originalFacts);
		var result = await Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, choice, Ct);
		var next = await Get(result.SuccessorTaskId); var old = await Get(source.Id);
		next.Recurrence!.StartDate.Should().Be(choice == SuccessorConflictResolution.MoveStartAfterFacts ? new(2026, 11, 28) : new(2026, 11, 16));
		old.Recurrence!.EndDate.Should().Be(next.Recurrence.StartDate.AddDays(-1));
		(await Facts()).Should().Equal(originalFacts);
		var retryCompletion = await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 20)), ct: Ct);
		retryCompletion.CompletedAt.Should().Be(completedAt); // survives even outside shortened bounds
		(await Lifecycle.CancelOccurrenceAsync(new(source.Id, new(2026, 11, 27)), Ct)).Should().Be(originalFacts[1]);
		if (choice == SuccessorConflictResolution.KeepStartAsSeparateWork)
		{
			await Lifecycle.CompleteOccurrenceAsync(new(next.Id, new(2026, 11, 16)), ct: Ct);
			(await Facts()).Should().HaveCount(3).And.Contain(originalFacts);
		}
	}

	[Theory]
	[InlineData("new-fact")]
	[InlineData("corrected-fact")]
	[InlineData("title")]
	[InlineData("closure")]
	public async Task StaleChoiceCannotMutateAnything(string change)
	{
		var source = await Seed(); var request = Change();
		if (change == "corrected-fact")
		{
			await using var db = await _factory.CreateAsync(Ct);
			db.TaskOccurrenceFacts.Add(new() { TaskId = source.Id, OccurrenceDate = new(2026, 11, 20), Status = TaskOccurrenceStatus.Skipped });
			await db.SaveChangesAsync(Ct);
		}
		var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		if (change is "new-fact" or "corrected-fact") await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 20)), ct: Ct);
		if (change == "title") { var task = await Get(source.Id); task.SetTitle("Changed"); await Repository.UpdateAsync(task, Ct); }
		if (change == "closure") await Lifecycle.EndSeriesAsync(source.Id, Ct);
		var before = await Get(source.Id); var facts = await Facts();
		var action = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		await action.Should().ThrowAsync<InvalidOperationException>();
		(await Get(source.Id)).Should().BeEquivalentTo(before); (await Facts()).Should().Equal(facts);
		await using var check = await _factory.CreateAsync(Ct);
		(await check.TaskItems.CountAsync(Ct)).Should().Be(1); (await check.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(0);
		if (change == "new-fact")
		{
			var fresh = await Successors.PreviewAsync(source.Id, request, Ct);
			fresh.AffectedFacts.Should().ContainSingle(); fresh.PreviewToken.Should().NotBe(preview.PreviewToken);
			await Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, fresh.PreviewToken, SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		}
	}

	[Fact]
	public async Task ChangedRequestOrForgedTokenCannotApplyThePreview()
	{
		var source = await Seed(); var request = Change(); var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		var changed = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request with { StartDate = new(2026, 11, 17) }, preview.PreviewToken, ct: Ct);
		await changed.Should().ThrowAsync<InvalidOperationException>();
		var forged = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, "arbitrary token", ct: Ct);
		await forged.Should().ThrowAsync<InvalidOperationException>();
		(await Get(source.Id)).Should().BeEquivalentTo(source);
	}

	[Theory]
	[InlineData(RecurringSuccessorKind.Replace)]
	[InlineData(RecurringSuccessorKind.Resume)]
	public async Task ConcurrentMatchingRequestsReturnOneSuccessorAndRetryRemainsStableAfterAnotherChange(RecurringSuccessorKind kind)
	{
		var source = await Seed(); if (kind == RecurringSuccessorKind.Resume) await Lifecycle.EndSeriesAsync(source.Id, Ct);
		var request = Change() with { Kind = kind }; var key = Guid.NewGuid();
		var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
			Successors.ApplyAsync(source.Id, key, request, preview.PreviewToken, ct: Ct), Ct)));
		results.Distinct().Should().ContainSingle();
		var first = results[0]; var second = await Apply(first.SuccessorTaskId, Change(new(2026, 11, 23)));
		(await Successors.ApplyAsync(source.Id, key, request, preview.PreviewToken, ct: Ct)).Should().Be(first);
		(await Get(source.Id)).RecurrenceSuccessorTaskId.Should().Be(first.SuccessorTaskId);
		var middle = await Get(first.SuccessorTaskId);
		middle.RecurrencePredecessorTaskId.Should().Be(source.Id); middle.RecurrenceSuccessorTaskId.Should().Be(second.SuccessorTaskId);
		(await Get(second.SuccessorTaskId)).RecurrencePredecessorTaskId.Should().Be(middle.Id);
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.CountAsync(Ct)).Should().Be(3); (await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(2);
		var reuse = () => Successors.ApplyAsync(source.Id, key, request with { ClearEndDate = true }, preview.PreviewToken, ct: Ct);
		await reuse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different inputs*");
		var reuseChoice = () => Successors.ApplyAsync(source.Id, key, request, preview.PreviewToken, SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		await reuseChoice.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different inputs*");
	}

	[Fact]
	public async Task ConcurrentDifferentRequestKeysCannotCreateSiblings()
	{
		var source = await Seed(); var request = Change(); var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
		{
			try { await Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, ct: Ct); return true; }
			catch (InvalidOperationException) { return false; }
		}, Ct)));
		results.Count(r => r).Should().Be(1);
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.CountAsync(Ct)).Should().Be(2); (await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(1);
	}

	[Fact]
	public async Task FailureAfterStagingEverythingRollsBackAndSameRequestCanRetry()
	{
		var source = await Seed(); var request = Change(); var preview = await Successors.PreviewAsync(source.Id, request, Ct); var key = Guid.NewGuid();
		var failingFactory = new Factory(Path.Combine(_directory, "planner.db"), new FailAfterSave());
		var failing = new RecurringTaskSuccessors(new RecurringTaskStore(failingFactory));
		var action = () => failing.ApplyAsync(source.Id, key, request, preview.PreviewToken, ct: Ct);
		await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated failure");
		(await Get(source.Id)).Should().BeEquivalentTo(source);
		await using (var db = await _factory.CreateAsync(Ct))
		{
			(await db.TaskItems.CountAsync(Ct)).Should().Be(1); (await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(0);
		}
		await Successors.ApplyAsync(source.Id, key, request, preview.PreviewToken, ct: Ct);
	}

	[Fact]
	public async Task RequestKeyCannotBeReusedForAnotherSeriesInTheSameTenant()
	{
		var first = await Seed(); var second = await Seed(); var request = Change(); var key = Guid.NewGuid();
		var preview = await Successors.PreviewAsync(first.Id, request, Ct);
		await Successors.ApplyAsync(first.Id, key, request, preview.PreviewToken, ct: Ct);
		var otherPreview = await Successors.PreviewAsync(second.Id, request, Ct);
		var reuse = () => Successors.ApplyAsync(second.Id, key, request, otherPreview.PreviewToken, ct: Ct);
		await reuse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different inputs*");
		(await Get(second.Id)).Should().BeEquivalentTo(second);
	}

	[Fact]
	public async Task MigrationAddsNullLinksWithoutChangingExistingSeriesOrOccurrenceHistory()
	{
		// Upgrade the existing stage-2 database, including a real terminal fact.
		await using var db = await _factory.CreateAsync(Ct);
		await db.GetService<IMigrator>().MigrateAsync("20261112215654_AddRecurringTaskLifecycle", Ct);
		var listId = Guid.NewGuid(); var taskId = Guid.NewGuid(); var now = _clock.Now;
		const string schedule = """{"Frequency":0,"Interval":1,"WeekStart":1,"Weekdays":[5],"MonthDays":[],"StartDate":"2026-11-13","EndDate":null,"MissedPolicy":1}""";
		await db.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO TaskLists (Id, Title, IsArchived, IsSystem, CreatedAt, UpdatedAt)
			VALUES ({listId}, {"List"}, {false}, {false}, {now}, {now})
			""", Ct);
		await db.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO TaskItems (Id, Title, TaskListId, ActiveTaskListId, IsCompleted, IsImportant, CreatedAt, UpdatedAt, Recurrence, RecurrenceRevision)
			VALUES ({taskId}, {"Original"}, {listId}, {listId}, {false}, {true}, {now}, {now}, {schedule}, {7})
			""", Ct);
		await db.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO TaskOccurrenceFacts (TaskId, OccurrenceDate, Status, CompletedAt)
			VALUES ({taskId}, {new DateOnly(2026, 11, 20)}, {0}, {now.ToString("O")})
			""", Ct);
		await db.Database.MigrateAsync(Ct);
		var task = await db.TaskItems.SingleAsync(Ct); task.RecurrenceRevision.Should().Be(7);
		task.RecurrencePredecessorTaskId.Should().BeNull(); task.RecurrenceSuccessorTaskId.Should().BeNull();
		task.Recurrence!.StartDate.Should().Be(new DateOnly(2026, 11, 13)); task.IsImportant.Should().BeTrue();
		var fact = (await Facts()).Single(); fact.Id.Should().Be(new TaskOccurrenceId(taskId, new(2026, 11, 20)));
		fact.CompletedAt.Should().Be(now); fact.Status.Should().Be(TaskOccurrenceStatus.Completed);
		(await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(0);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ExplicitAndAutomaticClosureResumeWithoutGapWorkOrReopeningHistory(bool automatic)
	{
		var source = await Seed(MissedOccurrencePolicy.SkipExpiredPeriods, end: automatic ? new(2026, 11, 13) : null);
		if (automatic) { _clock.Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc); await Lifecycle.ReconcileAsync(Ct); }
		else await Lifecycle.EndSeriesAsync(source.Id, Ct);
		var before = await Facts(); _clock.Now = new(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc);
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Resume, new(2026, 12, 7));
		var result = await Apply(source.Id, request);
		var old = await Get(source.Id); var successor = await Get(result.SuccessorTaskId);
		old.IsCompleted.Should().BeTrue(); old.CompletedAt.Should().BeNull(); successor.IsCompleted.Should().BeFalse();
		successor.Recurrence!.StartDate.Should().Be(request.StartDate); successor.Recurrence.EndDate.Should().BeNull();
		successor.Recurrence.Rule.Should().Be(source.Recurrence!.Rule);
		await Lifecycle.ReconcileAsync(Ct); (await Facts()).Should().Equal(before); // no November break obligations/skips
		if (automatic)
		{
			old.Recurrence!.EndDate.Should().Be(new DateOnly(2026, 11, 13));
			await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 13)), ct: Ct);
			(await Get(source.Id)).IsCompleted.Should().BeTrue();
			(await Get(source.Id)).RecurrenceSuccessorTaskId.Should().Be(successor.Id);
		}
		var storedService = new TaskItemService(Repository, new TaskListRepository(_factory), Lifecycle);
		var reopen = () => storedService.ReopenAsync(source.Id, Ct);
		await reopen.Should().ThrowAsync<InvalidOperationException>().WithMessage("*explicit successor and start date*");
	}

	[Fact]
	public async Task ResumeReusesFutureFactConflictPolicyAndAllowsExplicitRulePolicyAndEndChanges()
	{
		var source = await Seed(); var original = await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 20)), ct: Ct);
		await Lifecycle.EndSeriesAsync(source.Id, Ct);
		var request = new RecurringSuccessorRequest(RecurringSuccessorKind.Resume, new(2026, 11, 16),
			RecurrenceRule.Monthly(2, [31]), MissedOccurrencePolicy.SkipExpiredPeriods, new(2027, 4, 30));
		var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		preview.AffectedFacts.Should().ContainSingle().Which.Should().Be(original);
		var result = await Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken,
			SuccessorConflictResolution.KeepStartAsSeparateWork, Ct);
		var next = await Get(result.SuccessorTaskId);
		next.Recurrence!.Rule.Should().Be(request.Rule); next.Recurrence.EndDate.Should().Be(request.EndDate);
		next.Recurrence.MissedPolicy.Should().Be(MissedOccurrencePolicy.SkipExpiredPeriods);
		(await Facts()).Should().Equal(original); (await Get(source.Id)).IsCompleted.Should().BeTrue();
	}

	[Fact]
	public async Task PreviewIsReadOnlyEvenWithYearsOfUnreconciledExpiry()
	{
		var source = await Seed(MissedOccurrencePolicy.SkipExpiredPeriods, new(2020, 1, 1));
		var first = await Successors.PreviewAsync(source.Id, Change(), Ct);
		var second = await Successors.PreviewAsync(source.Id, Change(), Ct);
		second.Should().BeEquivalentTo(first); (await Get(source.Id)).Should().BeEquivalentTo(source); (await Facts()).Should().BeEmpty();
		await using var db = await _factory.CreateAsync(Ct);
		(await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(0);
	}

	[Fact]
	public async Task EarlierEndIsNeverExtendedAndEmptyRetiredRangeRoundTripsAndCloses()
	{
		var source = await Seed(start: new(2026, 12, 1), end: new(2026, 12, 10));
		var result = await Apply(source.Id, Change() with { ClearEndDate = true });
		var old = await Get(source.Id); old.Recurrence!.IsEmpty.Should().BeTrue();
		new RecurrenceCalculator().Enumerate(old.Recurrence, new(new(2026, 1, 1), new(2027, 1, 1))).Should().BeEmpty();
		_clock.Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc); await Lifecycle.ReconcileAsync(Ct);
		(await Get(source.Id)).IsCompleted.Should().BeTrue(); (await Facts()).Should().BeEmpty();
		var bounded = await Seed(end: new(2026, 11, 13));
		await Apply(bounded.Id, Change() with { ClearEndDate = true });
		(await Get(bounded.Id)).Recurrence!.EndDate.Should().Be(new DateOnly(2026, 11, 13));
		(await Get(result.SuccessorTaskId)).IsCompleted.Should().BeFalse();
	}

	[Fact]
	public async Task ProposedBoundsAndUnavailableLaterChoiceFailWithoutShorteningTheParent()
	{
		var source = await Seed(end: new(2026, 11, 20));
		await Lifecycle.CompleteOccurrenceAsync(new(source.Id, new(2026, 11, 20)), ct: Ct);
		var request = Change(); var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		preview.LaterSchedule.Should().BeNull();
		var later = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, SuccessorConflictResolution.MoveStartAfterFacts, Ct);
		await later.Should().ThrowAsync<InvalidOperationException>();
		var invalid = () => Successors.PreviewAsync(source.Id, request with { EndDate = new(2026, 11, 15) }, Ct);
		await invalid.Should().ThrowAsync<ArgumentException>();
		(await Get(source.Id)).Should().BeEquivalentTo(source);
		await Lifecycle.EndSeriesAsync(source.Id, Ct);
		var invalidResume = () => Successors.PreviewAsync(source.Id, request with { Kind = RecurringSuccessorKind.Resume, StartDate = new(2026, 12, 1), EndDate = new(2026, 11, 20) }, Ct);
		await invalidResume.Should().ThrowAsync<ArgumentException>();
	}

	[Theory]
	[InlineData("ordinary")]
	[InlineData("trash")]
	[InlineData("archive")]
	[InlineData("wrong-intent")]
	public async Task IneligibleSourceCannotPreviewOrApply(string kind)
	{
		var source = await Seed(); var request = Change(); var preview = await Successors.PreviewAsync(source.Id, request, Ct);
		if (kind == "ordinary") { source = new("Ordinary", source.TaskListId); await Repository.AddAsync(source, Ct); }
		if (kind == "trash") await Lifecycle.TrashSeriesAsync(source.Id, Ct);
		if (kind == "archive")
		{
			await using var db = await _factory.CreateAsync(Ct);
			(await db.TaskLists.SingleAsync(Ct)).Archive(); await db.SaveChangesAsync(Ct);
		}
		if (kind == "wrong-intent") request = request with { Kind = RecurringSuccessorKind.Resume };
		var check = () => Successors.PreviewAsync(source.Id, request, Ct);
		await check.Should().ThrowAsync<Exception>();
		var apply = () => Successors.ApplyAsync(source.Id, Guid.NewGuid(), request, preview.PreviewToken, ct: Ct);
		await apply.Should().ThrowAsync<Exception>();
	}

	[Fact]
	public async Task RestoringTrashedSeriesRequiresExplicitResumeAndKeepsTheBreakEmpty()
	{
		var source = await Seed(); await Lifecycle.TrashSeriesAsync(source.Id, Ct);
		_clock.Now = new(2026, 12, 7, 12, 0, 0, DateTimeKind.Utc);
		await new TaskItemService(Repository, new TaskListRepository(_factory), Lifecycle).RestoreAsync(source.Id, Ct);
		var result = await Apply(source.Id, new(RecurringSuccessorKind.Resume, new(2026, 12, 7)));
		await Lifecycle.ReconcileAsync(Ct); (await Facts()).Should().BeEmpty();
		(await Get(source.Id)).IsCompleted.Should().BeTrue(); (await Get(result.SuccessorTaskId)).IsCompleted.Should().BeFalse();
	}

	[Fact]
	public async Task DetachedWritesCannotUndoTheCutoffOrLoseTheLinkEvenOnRepeatedRetries()
	{
		var stale = await Seed(); var result = await Apply(stale.Id, Change()); var saved = await Get(stale.Id);
		stale.SetTitle("Stale edit");
		for (var retry = 0; retry < 3; retry++)
		{
			var write = () => Repository.UpdateAsync(stale, Ct); await write.Should().ThrowAsync<DbUpdateConcurrencyException>();
		}
		(await Get(stale.Id)).Should().BeEquivalentTo(saved);
		(await Get(result.SuccessorTaskId)).RecurrencePredecessorTaskId.Should().Be(stale.Id);
	}

	[Fact]
	public async Task PermanentRemovalDoesNotDeleteTheOtherSeriesAndCleansItsRequestMetadata()
	{
		var source = await Seed(); var result = await Apply(source.Id, Change());
		await Lifecycle.TrashSeriesAsync(source.Id, Ct); await Repository.PermanentlyDeleteAsync(source.Id, Ct);
		(await Get(result.SuccessorTaskId)).RecurrencePredecessorTaskId.Should().Be(source.Id);
		await using var db = await _factory.CreateAsync(Ct);
		(await db.TaskItems.CountAsync(Ct)).Should().Be(1); (await db.RecurringSuccessorOperations.CountAsync(Ct)).Should().Be(0);
	}

	private sealed class FailAfterSave : SaveChangesInterceptor
	{
		public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
			=> throw new InvalidOperationException("Simulated failure");
	}
	private sealed class Clock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
	private sealed class Factory(string path, SaveChangesInterceptor? interceptor = null) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AppDbContext(
			new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False")
				.AddInterceptors(interceptor is null ? [] : [interceptor]).Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
