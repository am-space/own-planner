using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OwnPlanner.Application.Calendar;
using OwnPlanner.Application.Tasks;
using OwnPlanner.Application.Tasks.Recurrence;
using OwnPlanner.Application.WeeklyReviews;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;
using OwnPlanner.Infrastructure.Tasks;
using OwnPlanner.Infrastructure.WeeklyReviews;

namespace OwnPlanner.Infrastructure.Tests.Tasks;

public sealed class RecurringTaskLifecycleTests : IAsyncLifetime
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ownplanner-recurrence-{Guid.NewGuid():N}");
	private readonly Clock _clock = new(new(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc));
	private Factory _factory = null!;
	private CancellationToken Ct => TestContext.Current.CancellationToken;
	private RecurringTaskLifecycle Service => CreateService();
	private RecurringTaskLifecycle CreateService(IRecurringTaskStore? store = null) => new(store ?? new RecurringTaskStore(_factory),
		new PlanningCalendar(new WeeklyReviewStore(_factory), _clock), new RecurrenceCalculator());
	private TaskItemRepository Repository => new(_factory);

	public async ValueTask InitializeAsync()
	{
		Directory.CreateDirectory(_directory);
		_factory = new(Path.Combine(_directory, "planner.db"));
		await using var db = await _factory.CreateAsync(Ct);
		await db.Database.MigrateAsync(Ct);
	}
	public ValueTask DisposeAsync()
	{
		Directory.Delete(_directory, true);
		return ValueTask.CompletedTask;
	}
	private async Task<TaskItem> Seed(MissedOccurrencePolicy policy = MissedOccurrencePolicy.SkipExpiredPeriods,
		DateOnly? start = null, DateOnly? end = null, RecurrenceRule? rule = null)
	{
		await using var db = await _factory.CreateAsync(Ct);
		var list = new TaskList("Private list");
		var task = new TaskItem("Private training", list.Id, "Description", _clock.Now.AddDays(10), true);
		task.SetFocusAt(_clock.Now.Date);
		task.ConfigureRecurrence(new(rule ?? RecurrenceRule.Weekly(1, [DayOfWeek.Friday], DayOfWeek.Monday),
			start ?? new(2026, 11, 13), end, policy));
		db.AddRange(list, task);
		await db.SaveChangesAsync(Ct);
		return task;
	}
	private async Task<TaskOccurrenceFactRow[]> Facts()
	{
		await using var db = await _factory.CreateAsync(Ct);
		return await db.TaskOccurrenceFacts.AsNoTracking().OrderBy(f => f.OccurrenceDate).ToArrayAsync(Ct);
	}

	[Fact]
	public async Task MigrationPreservesAllOrdinaryFieldsAndAddsOnlyNullableConfigurationAndZeroRevision()
	{
		var factory = new Factory(Path.Combine(_directory, "upgrade.db"));
		var id = Guid.NewGuid(); var listId = Guid.NewGuid(); var now = _clock.Now;
		await using var db = await factory.CreateAsync(Ct);
		await db.GetService<IMigrator>().MigrateAsync("20261001123424_AddPausedGoals", Ct);
		await db.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO TaskLists (Id, Title, IsArchived, IsSystem, CreatedAt, UpdatedAt)
			VALUES ({listId}, {"List"}, {false}, {false}, {now}, {now})
			""", Ct);
		await db.Database.ExecuteSqlInterpolatedAsync($"""
			INSERT INTO TaskItems (Id, Title, Description, IsCompleted, IsImportant, CreatedAt, UpdatedAt,
			 DueAt, CompletedAt, TaskListId, ActiveTaskListId, TrashedAt, FocusAt, GoalId)
			VALUES ({id}, {"Original"}, {"Details"}, {true}, {true}, {now}, {now},
			 {now}, {now}, {listId}, {listId}, {null}, {now}, {Guid.NewGuid()})
			""", Ct);
		async Task<object[]> Fields()
		{
			await db.Database.OpenConnectionAsync(Ct);
			await using var command = db.Database.GetDbConnection().CreateCommand();
			command.CommandText = "SELECT Id, Title, Description, IsCompleted, IsImportant, CreatedAt, UpdatedAt, DueAt, CompletedAt, TaskListId, ActiveTaskListId, TrashedAt, FocusAt, GoalId FROM TaskItems";
			await using var reader = await command.ExecuteReaderAsync(Ct);
			await reader.ReadAsync(Ct);
			var values = new object[reader.FieldCount]; reader.GetValues(values); return values;
		}
		var before = await Fields();
		await db.Database.MigrateAsync(Ct);
		(await Fields()).Should().Equal(before);
		var task = await db.TaskItems.SingleAsync(Ct);
		task.Recurrence.Should().BeNull();
		task.RecurrenceCheckedBefore.Should().BeNull();
		task.RecurrenceCalendarKey.Should().BeNull();
		task.RecurrenceRevision.Should().Be(0);
		task.Reopen(); await db.SaveChangesAsync(Ct);
		task.IsCompleted.Should().BeFalse(); task.CompletedAt.Should().BeNull();
	}

	[Fact]
	public async Task DateOnlyAndCompletionRoundTripAsSeparateValuesWithUniqueIdentity()
	{
		var task = await Seed();
		var date = new DateOnly(2026, 11, 20); // early completion is explicit; no date-to-instant inference
		var actual = _clock.Now.AddHours(-3);
		await Service.CompleteOccurrenceAsync(new(task.Id, date), actual, Ct);
		var saved = (await Facts()).Single();
		saved.OccurrenceDate.Should().Be(date); saved.CompletedAt.Should().Be(actual);
		saved.CompletedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
		await using var db = await _factory.CreateAsync(Ct);
		await db.Database.OpenConnectionAsync(Ct);
		await using var command = db.Database.GetDbConnection().CreateCommand();
		command.CommandText = "SELECT OccurrenceDate, json_extract(Recurrence, '$.StartDate'), TaskOccurrenceFacts.CompletedAt FROM TaskOccurrenceFacts JOIN TaskItems ON TaskItems.Id = TaskOccurrenceFacts.TaskId";
		await using (var reader = await command.ExecuteReaderAsync(Ct))
		{
			await reader.ReadAsync(Ct);
			reader.GetString(0).Should().Be("2026-11-20"); reader.GetString(1).Should().Be("2026-11-13");
			reader.GetString(2).Should().Be(actual.ToString("O"));
		}
		db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = date, Status = TaskOccurrenceStatus.Cancelled });
		var duplicate = () => db.SaveChangesAsync(Ct);
		await duplicate.Should().ThrowAsync<DbUpdateException>();
		var parent = await Repository.GetAsync(task.Id, Ct);
		parent!.IsCompleted.Should().BeFalse(); parent.CompletedAt.Should().BeNull();
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(1, true)]
	[InlineData(2, true)]
	[InlineData(99, false)]
	public async Task DatabaseRejectsInvalidStatusTimestampCombinations(int status, bool timestamp)
	{
		var task = await Seed();
		await using var db = await _factory.CreateAsync(Ct);
		DateTime? actual = timestamp ? _clock.Now : null;
		var insert = () => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TaskOccurrenceFacts (TaskId, OccurrenceDate, Status, CompletedAt) VALUES ({task.Id}, {new DateOnly(2026, 11, 13)}, {status}, {actual})", Ct);
		await insert.Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>();
	}

	[Fact]
	public async Task LateDefaultCompletionCorrectsSkipAndRetriesKeepFirstActualInstant()
	{
		var task = await Seed();
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		var id = new TaskOccurrenceId(task.Id, new(2026, 11, 13));
		var first = await Service.CompleteOccurrenceAsync(id, ct: Ct);
		_clock.Now = _clock.Now.AddDays(10);
		var retry = await Service.CompleteOccurrenceAsync(id, _clock.Now.AddHours(-1), Ct);
		retry.Should().Be(first); first.CompletedAt.Should().Be(new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));
		(await Facts()).Single(f => f.OccurrenceDate == id.OccurrenceDate).Status.Should().Be(TaskOccurrenceStatus.Completed);
	}

	[Fact]
	public async Task AutomaticallyClosedHistoryCanBeCorrectedWithoutReopeningParent()
	{
		var task = await Seed(end: new(2026, 11, 13));
		_clock.Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		var closed = await Repository.GetAsync(task.Id, Ct);
		closed!.IsCompleted.Should().BeTrue(); closed.CompletedAt.Should().BeNull();
		var actual = _clock.Now.AddDays(-2);
		var fact = await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), actual, Ct);
		fact.Status.Should().Be(TaskOccurrenceStatus.Completed); fact.CompletedAt.Should().Be(actual);
		(await Repository.GetAsync(task.Id, Ct)).Should().BeEquivalentTo(closed);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Completed);
	}

	[Fact]
	public async Task ClosedHistoricalSkipOutsideGenerationBoundsKeepsOriginalIdentityOnCorrection()
	{
		var task = await Seed(end: new(2026, 11, 13));
		var date = new DateOnly(2026, 11, 20);
		// Represents preserved predecessor history after a later stage shortens its generation bounds.
		await using (var db = await _factory.CreateAsync(Ct))
		{
			task.Complete(); db.Update(task);
			db.TaskOccurrenceFacts.Add(new() { TaskId = task.Id, OccurrenceDate = date, Status = TaskOccurrenceStatus.Skipped });
			await db.SaveChangesAsync(Ct);
		}
		var completed = await Service.CompleteOccurrenceAsync(new(task.Id, date), ct: Ct);
		completed.Id.Should().Be(new TaskOccurrenceId(task.Id, date));
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeTrue();
		(await Facts()).Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Completed);
	}

	[Fact]
	public async Task MonthlyPersistenceClampsAndDeduplicatesWithoutLosingTheOriginalSelection()
	{
		_clock.Now = new(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
		var task = await Seed(start: new(2024, 2, 1), end: new(2024, 2, 29), rule: RecurrenceRule.Monthly(1, [29, 30, 31]));
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(2024, 2, 29));
		var saved = (await Repository.GetAsync(task.Id, Ct))!;
		saved.IsCompleted.Should().BeTrue();
		saved.Recurrence.Should().Be(task.Recurrence);
		saved.Recurrence!.Rule.MonthDays.Should().Equal(29, 30, 31);
	}

	[Fact]
	public async Task StoredTaskCompletionUsesSharedTerminationAndOrdinaryCompletionStillRecordsWork()
	{
		var recurring = await Seed(start: new(2026, 11, 6));
		var ordinary = new TaskItem("One-off", recurring.TaskListId);
		await Repository.AddAsync(ordinary, Ct);
		var service = new TaskItemService(Repository, new TaskListRepository(_factory), Service);
		await service.CompleteAsync(recurring.Id, Ct); await service.CompleteAsync(ordinary.Id, Ct);
		(await Facts()).Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Skipped);
		(await Repository.GetAsync(recurring.Id, Ct))!.CompletedAt.Should().BeNull();
		(await Repository.GetAsync(ordinary.Id, Ct))!.CompletedAt.Should().NotBeNull();
		var reopen = () => service.ReopenAsync(recurring.Id, Ct);
		await reopen.Should().ThrowAsync<InvalidOperationException>();
		await service.ReopenAsync(ordinary.Id, Ct);
		(await Repository.GetAsync(ordinary.Id, Ct))!.IsCompleted.Should().BeFalse();
	}

	[Fact]
	public async Task ArchivedListDoesNotSuspendItsCalendarScheduleOrBlockBackgroundExpiry()
	{
		var task = await Seed(end: new(2026, 11, 13));
		await using (var db = await _factory.CreateAsync(Ct))
		{
			(await db.TaskLists.SingleAsync(Ct)).Archive(); await db.SaveChangesAsync(Ct);
		}
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Skipped);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeTrue();
		var correction = () => Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), ct: Ct);
		await correction.Should().ThrowAsync<InvalidOperationException>();
	}

	[Theory]
	[InlineData(MissedOccurrencePolicy.SkipExpiredPeriods)]
	[InlineData(MissedOccurrencePolicy.RetainEachObligation)]
	public async Task DowntimeCatchupRespectsPolicyAndFullPeriodBeforeClosure(MissedOccurrencePolicy policy)
	{
		var start = new DateOnly(2024, 1, 1);
		var task = await Seed(policy, start, new(2026, 11, 13), RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday));
		await Service.ReconcileAsync(Ct);
		var count = policy == MissedOccurrencePolicy.SkipExpiredPeriods ? new DateOnly(2026, 11, 9).DayNumber - start.DayNumber : 0;
		(await Facts()).Should().HaveCount(count).And.OnlyContain(f => f.Status == TaskOccurrenceStatus.Skipped && f.CompletedAt == null);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeFalse();
		_clock.Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().Be(policy == MissedOccurrencePolicy.SkipExpiredPeriods);
		var snapshot = await Facts();
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().BeEquivalentTo(snapshot);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BoundedRetainedSeriesClosesOnlyAfterEveryDateIsResolvedAndEndHasPassed(bool cancelLast)
	{
		var task = await Seed(MissedOccurrencePolicy.RetainEachObligation, new(2026, 11, 6), new(2026, 11, 13));
		await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 6)), ct: Ct);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeFalse();
		if (cancelLast) await Service.CancelOccurrenceAsync(new(task.Id, new(2026, 11, 13)), Ct);
		else await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), ct: Ct);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeTrue();
		(await Facts()).Should().HaveCount(2);
	}

	[Fact]
	public async Task SkipPolicyCanCloseAfterEarlyResolutionsButNeverBeforeTheEndDatePasses()
	{
		var task = await Seed(end: new(2026, 11, 20));
		await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), ct: Ct);
		await Service.CancelOccurrenceAsync(new(task.Id, new(2026, 11, 20)), Ct);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeFalse();
		_clock.Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(task.Id, Ct))!.IsCompleted.Should().BeTrue();
		(await Facts()).Should().HaveCount(2);
	}

	[Fact]
	public async Task EmptyBoundClosesWithoutAFactAndUnboundedSeriesNeverAutoCloses()
	{
		var empty = await Seed(start: new(2026, 11, 14), end: new(2026, 11, 15));
		var endless = await Seed(start: new(2026, 11, 20));
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		(await Repository.GetAsync(empty.Id, Ct))!.IsCompleted.Should().BeTrue();
		(await Repository.GetAsync(endless.Id, Ct))!.IsCompleted.Should().BeFalse();
		(await Facts()).Should().BeEmpty();
	}

	[Fact]
	public async Task ExplicitEndFreezesGenerationAndPreservesEarlyFactsAndOrdinaryReopenCompatibility()
	{
		var task = await Seed();
		await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 12, 25)), ct: Ct);
		await Service.EndSeriesAsync(task.Id, Ct); await Service.EndSeriesAsync(task.Id, Ct);
		_clock.Now = _clock.Now.AddMonths(6);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(2026, 12, 25));
		var closed = (await Repository.GetAsync(task.Id, Ct))!;
		Action reopen = () => closed.Reopen();
		reopen.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData("ordinary", typeof(InvalidOperationException))]
	[InlineData("trash", typeof(KeyNotFoundException))]
	[InlineData("archive", typeof(InvalidOperationException))]
	[InlineData("closed", typeof(InvalidOperationException))]
	[InlineData("wrongDay", typeof(ArgumentException))]
	[InlineData("beforeStart", typeof(ArgumentException))]
	[InlineData("afterEnd", typeof(ArgumentException))]
	public async Task NewFactsRequireCurrentParentListAndScheduleEligibility(string state, Type expectedException)
	{
		var task = await Seed(end: new(2026, 11, 20));
		var date = new DateOnly(2026, 11, 13);
		await using (var db = await _factory.CreateAsync(Ct))
		{
			switch (state)
			{
				case "ordinary": task = new TaskItem("Ordinary", task.TaskListId); db.Add(task); break;
				case "trash": task.Trash(); db.Update(task); break;
				case "archive": var list = await db.TaskLists.SingleAsync(Ct); list.Archive(); break;
				case "closed": task.Complete(); db.Update(task); break;
				case "wrongDay": date = date.AddDays(1); break;
				case "beforeStart": date = date.AddDays(-7); break;
				case "afterEnd": date = date.AddDays(14); break;
			}
			await db.SaveChangesAsync(Ct);
		}
		var complete = () => Service.CompleteOccurrenceAsync(new(task.Id, date), ct: Ct);
		(await complete.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(expectedException);
		var cancel = () => Service.CancelOccurrenceAsync(new(task.Id, date), Ct);
		(await cancel.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(expectedException);
		(await Facts()).Should().BeEmpty();
	}

	[Fact]
	public async Task CancellationRetriesAreStableAndConflictingTerminalChangesFail()
	{
		var task = await Seed();
		var id = new TaskOccurrenceId(task.Id, new(2026, 11, 13));
		var cancel = await Service.CancelOccurrenceAsync(id, Ct);
		(await Service.CancelOccurrenceAsync(id, Ct)).Should().Be(cancel);
		var completeCancelled = () => Service.CompleteOccurrenceAsync(id, ct: Ct);
		await completeCancelled.Should().ThrowAsync<InvalidOperationException>();
		var completedId = new TaskOccurrenceId(task.Id, new(2026, 11, 20));
		await Service.CompleteOccurrenceAsync(completedId, ct: Ct);
		var cancelCompleted = () => Service.CancelOccurrenceAsync(completedId, Ct);
		await cancelCompleted.Should().ThrowAsync<InvalidOperationException>();
		_clock.Now = _clock.Now.AddDays(8);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Select(f => f.Status).Should().Equal(TaskOccurrenceStatus.Cancelled, TaskOccurrenceStatus.Completed);
	}

	[Fact]
	public async Task ConcurrentExpiryAndCompletionsConvergeOnFirstSuccessfulTimestamp()
	{
		var task = await Seed();
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		var id = new TaskOccurrenceId(task.Id, new(2026, 11, 13));
		var completions = Enumerable.Range(1, 6).Select(i => Task.Run(() => Service.CompleteOccurrenceAsync(id, _clock.Now.AddHours(-i), Ct), Ct)).ToArray();
		var reconciliations = Enumerable.Range(0, 3).Select(_ => Task.Run(() => Service.ReconcileAsync(Ct), Ct)).ToArray();
		var results = await Task.WhenAll(completions);
		await Task.WhenAll(reconciliations);
		results.Should().OnlyContain(f => f == results[0]);
		var saved = (await Facts()).Single();
		saved.Status.Should().Be(TaskOccurrenceStatus.Completed); saved.CompletedAt.Should().Be(results[0].CompletedAt);
	}

	[Fact]
	public async Task CommittedBatchesResumeAfterFailureAndNeverRevertPriorFacts()
	{
		var task = await Seed(start: new(2024, 1, 1), rule: RecurrenceRule.Weekly(1, Enum.GetValues<DayOfWeek>(), DayOfWeek.Monday));
		var faulty = new FailAfterFirstBatchStore(new RecurringTaskStore(_factory));
		var interrupted = () => CreateService(faulty).ReconcileAsync(Ct);
		await interrupted.Should().ThrowAsync<InvalidOperationException>();
		(await Facts()).Should().HaveCount(RecurringTaskLifecycle.BatchSize);
		var checkpoint = (await Repository.GetAsync(task.Id, Ct))!.RecurrenceCheckedBefore;
		checkpoint.Should().Be(new DateOnly(2024, 1, 1).AddDays(RecurringTaskLifecycle.BatchSize));
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().HaveCount(new DateOnly(2026, 11, 9).DayNumber - new DateOnly(2024, 1, 1).DayNumber);
	}

	[Fact]
	public async Task FailedTransactionRollsBackFactAndCheckpointTogether()
	{
		var task = await Seed();
		var store = new RecurringTaskStore(_factory);
		var failed = () => store.ExecuteAsync<bool>(task.Id, async (tx, ct) =>
		{
			await tx.PutFactAsync(new(new(task.Id, new(2026, 11, 13)), TaskOccurrenceStatus.Skipped), ct);
			tx.Task.CheckRecurrenceBefore(new(2026, 11, 16), "UTC|1");
			throw new InvalidOperationException("Interrupted batch");
		}, Ct);
		await failed.Should().ThrowAsync<InvalidOperationException>();
		(await Facts()).Should().BeEmpty();
		(await Repository.GetAsync(task.Id, Ct))!.RecurrenceCheckedBefore.Should().BeNull();
	}

	[Fact]
	public async Task CalendarPreferenceChangesRescanWithoutErasingRecordedHistoryOrChangingRuleAlignment()
	{
		var task = await Seed(start: new(2026, 11, 1));
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle().Which.OccurrenceDate.Should().Be(new DateOnly(2026, 11, 6));
		var reviews = new WeeklyReviewService(new WeeklyReviewStore(_factory), _clock);
		await reviews.ConfigureAsync(false, "UTC", 0, "18:00", ct: Ct);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().HaveCount(2);
		await reviews.ConfigureAsync(false, "America/Los_Angeles", 1, "18:00", ct: Ct);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().HaveCount(2); // resolved skips are not reinterpreted
		(await Repository.GetAsync(task.Id, Ct))!.Recurrence!.Rule.WeekStart.Should().Be(DayOfWeek.Monday);
	}

	[Theory]
	[InlineData("2026-03-09T03:59:59Z", false)]
	[InlineData("2026-03-09T04:00:00Z", true)]
	[InlineData("2026-11-02T04:59:59Z", false)]
	[InlineData("2026-11-02T05:00:00Z", true)]
	public async Task ReconciliationUsesLocalDstBoundaryRatherThanUtcMidnight(string now, bool expired)
	{
		_clock.Now = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
		var friday = _clock.Now.Month == 3 ? new DateOnly(2026, 3, 6) : new DateOnly(2026, 10, 30);
		await Seed(start: friday, end: friday);
		await new WeeklyReviewService(new WeeklyReviewStore(_factory), _clock).ConfigureAsync(false, "America/New_York", 1, "18:00", ct: Ct);
		await Service.ReconcileAsync(Ct);
		(await Facts()).Should().HaveCount(expired ? 1 : 0);
	}

	[Fact]
	public async Task CalendarAndTaskReadsLeaveExpiredFactsCheckpointAndParentUntouched()
	{
		var task = await Seed(start: new(2026, 1, 1), end: new(2026, 2, 1));
		await new PlanningCalendar(new WeeklyReviewStore(_factory), _clock).ResolveAsync("today", Ct);
		var read = (await Repository.GetAsync(task.Id, Ct))!;
		read.RecurrenceCheckedBefore.Should().BeNull(); read.IsCompleted.Should().BeFalse();
		(await Facts()).Should().BeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StaleDetachedWriteCannotUndoCheckpointOrClosure(bool asynchronous)
	{
		var task = await Seed(end: new(2026, 11, 13));
		var stale = (await Repository.GetAsync(task.Id, Ct))!;
		var revision = stale.RecurrenceRevision;
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		var reconciled = await Repository.GetAsync(task.Id, Ct);
		var facts = await Facts();
		stale.SetTitle("Stale edit");
		Func<Task> update = async () =>
		{
			if (asynchronous) await Repository.UpdateAsync(stale, Ct);
			else
			{
				await using var db = await _factory.CreateAsync(Ct);
				db.Update(stale);
				db.SaveChanges();
			}
		};
		for (var retry = 0; retry < 3; retry++)
			await update.Should().ThrowAsync<DbUpdateConcurrencyException>();
		stale.RecurrenceRevision.Should().Be(revision);
		var current = (await Repository.GetAsync(task.Id, Ct))!;
		current.Should().BeEquivalentTo(reconciled);
		(await Facts()).Should().BeEquivalentTo(facts);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailedConcurrencySaveRestoresRevisionWhenRetryingTheSameContext(bool asynchronous)
	{
		await Seed(end: new(2026, 11, 13));
		await using var db = await _factory.CreateAsync(Ct);
		var stale = await db.TaskItems.SingleAsync(Ct);
		var revision = stale.RecurrenceRevision;
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		await Service.ReconcileAsync(Ct);
		stale.SetTitle("Stale edit");
		for (var retry = 0; retry < 3; retry++)
		{
			var save = () => Save(db, asynchronous);
			await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
			stale.RecurrenceRevision.Should().Be(revision);
			db.Entry(stale).Property(t => t.RecurrenceRevision).OriginalValue.Should().Be(revision);
			db.Entry(stale).Property(t => t.RecurrenceRevision).IsModified.Should().BeFalse();
		}
		(await Repository.GetAsync(stale.Id, Ct))!.IsCompleted.Should().BeTrue();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailedWriteRestoresRevisionAndRepairCanSaveExactlyOneRevision(bool asynchronous)
	{
		var task = await Seed();
		await using var db = await _factory.CreateAsync(Ct);
		var tracked = await db.TaskItems.SingleAsync(Ct);
		var revision = tracked.RecurrenceRevision;
		tracked.AssignToList(Guid.NewGuid()); // Foreign key failure, rather than a stale token.
		var save = () => Save(db, asynchronous);
		await save.Should().ThrowAsync<DbUpdateException>();
		tracked.RecurrenceRevision.Should().Be(revision);
		tracked.AssignToList(task.TaskListId);
		await Save(db, asynchronous);
		tracked.RecurrenceRevision.Should().Be(revision + 1);
		(await Repository.GetAsync(task.Id, Ct))!.RecurrenceRevision.Should().Be(revision + 1);
	}

	[Fact]
	public async Task CancelledSaveRestoresRevisionAndCanBeRetriedWithAnActiveToken()
	{
		await Seed();
		await using var db = await _factory.CreateAsync(Ct);
		var tracked = await db.TaskItems.SingleAsync(Ct);
		var revision = tracked.RecurrenceRevision;
		tracked.SetTitle("Updated title");
		using var cancelled = new CancellationTokenSource();
		await cancelled.CancelAsync();
		var save = () => db.SaveChangesAsync(cancelled.Token);
		await save.Should().ThrowAsync<OperationCanceledException>();
		tracked.RecurrenceRevision.Should().Be(revision);
		await db.SaveChangesAsync(Ct);
		tracked.RecurrenceRevision.Should().Be(revision + 1);
		(await Repository.GetAsync(tracked.Id, Ct))!.Title.Should().Be("Updated title");
	}

	private Task<int> Save(AppDbContext db, bool asynchronous) => asynchronous
		? db.SaveChangesAsync(Ct) : Task.FromResult(db.SaveChanges());

	[Fact]
	public async Task TrashRestorePreservesFactsAndClosureAndPermanentDeletionCascadesHistory()
	{
		var task = await Seed();
		await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), ct: Ct);
		task = (await Repository.GetAsync(task.Id, Ct))!; task.Trash(); await Repository.UpdateAsync(task, Ct);
		(await Facts()).Should().ContainSingle();
		await Repository.RestoreAsync(task.Id, Ct);
		task = (await Repository.GetAsync(task.Id, Ct))!; task.IsCompleted.Should().BeTrue();
		_clock.Now = _clock.Now.AddMonths(2); await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle();
		task.Trash(); await Repository.UpdateAsync(task, Ct);
		(await Repository.PermanentlyDeleteAsync(task.Id, Ct)).Should().Be(TaskPermanentDeleteResult.Deleted);
		(await Facts()).Should().BeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StoredTrashCatchesUpBeforeStoppingEvenInArchivedListsWithoutGeneratingInactiveGap(bool archived)
	{
		var task = await Seed();
		if (archived)
		{
			await using var db = await _factory.CreateAsync(Ct);
			(await db.TaskLists.SingleAsync(Ct)).Archive(); await db.SaveChangesAsync(Ct);
		}
		_clock.Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
		var tasks = new TaskItemService(Repository, new TaskListRepository(_factory), Service);
		await tasks.DeleteAsync(task.Id, Ct); await tasks.DeleteAsync(task.Id, Ct);
		(await Facts()).Should().ContainSingle().Which.Status.Should().Be(TaskOccurrenceStatus.Skipped);
		(await Repository.GetTrashedAsync(task.Id, Ct))!.IsCompleted.Should().BeTrue();
		await tasks.RestoreAsync(task.Id, Ct);
		_clock.Now = _clock.Now.AddMonths(3); await Service.ReconcileAsync(Ct);
		(await Facts()).Should().ContainSingle();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DeletingListCascadesActiveFactsButPreservesTrashedFacts(bool trash)
	{
		var task = await Seed();
		await Service.CompleteOccurrenceAsync(new(task.Id, new(2026, 11, 13)), ct: Ct);
		if (trash) { task.Trash(); await Repository.UpdateAsync(task, Ct); }
		await using var db = await _factory.CreateAsync(Ct);
		await db.TaskLists.ExecuteDeleteAsync(Ct);
		(await Facts()).Should().HaveCount(trash ? 1 : 0);
	}

	private sealed class FailAfterFirstBatchStore(IRecurringTaskStore inner) : IRecurringTaskStore
	{
		private int _calls;
		public Task<IReadOnlyList<Guid>> ListOpenSeriesIdsAsync(CancellationToken ct = default) => inner.ListOpenSeriesIdsAsync(ct);
		public Task<T> ExecuteAsync<T>(Guid taskId, Func<IRecurringTaskTransaction, CancellationToken, Task<T>> work, CancellationToken ct = default)
		{
			if (++_calls == 2) throw new InvalidOperationException("Simulated host failure between batches");
			return inner.ExecuteAsync(taskId, work, ct);
		}
	}
	private sealed class Clock(DateTime now) : TimeProvider
	{
		public DateTime Now { get; set; } = now;
		public override DateTimeOffset GetUtcNow() => new(Now);
	}
	private sealed class Factory(string path) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new AppDbContext(
			new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options));
		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
