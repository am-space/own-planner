using FluentAssertions;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public class ReflectionReportReaderTests
{
	private static readonly DateTime AsOfUtc = Utc(2026, 8, 19, 12);
	private static readonly DateTime PeriodStartUtc = Utc(2026, 8, 12, 12);

	[Fact]
	public async Task GetAsync_EmptyDatabaseUsesInjectedInstantAndExplicitSemantics()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(), TestContext.Current.CancellationToken);

		report.AsOfUtc.Should().Be(AsOfUtc);
		report.PeriodStartUtc.Should().Be(PeriodStartUtc);
		report.PeriodEndExclusiveUtc.Should().Be(AsOfUtc);
		report.TimeZone.Should().Be("UTC");
		report.PeriodSemantics.Should().Be("[periodStartUtc, periodEndExclusiveUtc)");
		report.HistoricalLimitations.Should().Contain(item => item.Contains("reopened", StringComparison.OrdinalIgnoreCase));
		report.Totals.Should().Be(new ReflectionOverallTotals(0, 0, 0, 0, 0, 0));
	}

	[Fact]
	public async Task GetAsync_FiltersTaskAndNoteRowsInSqlToRequiredCurrentStateAndPeriod()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var interceptor = new CommandCaptureInterceptor();
		var reader = new ReflectionReportReader(new CapturingDbContextFactory(connection, interceptor), new FixedTimeProvider(AsOfUtc));

		await reader.GetAsync(new ReflectionReportOptions(), TestContext.Current.CancellationToken);

		var taskQuery = interceptor.Commands.Single(command => command.Contains("FROM \"TaskItems\"", StringComparison.Ordinal));
		taskQuery.Should().Contain("IsCompleted").And.Contain("CreatedAt").And.Contain("CompletedAt");
		var noteQuery = interceptor.Commands.Single(command => command.Contains("FROM \"NoteItems\"", StringComparison.Ordinal));
		noteQuery.Should().Contain("NoteListId").And.Contain("CreatedAt").And.Contain("UpdatedAt");
	}

	[Fact]
	public async Task GetAsync_UsesCurrentStateAndExactHalfOpenBoundaries()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var coveredGoal = new Goal("Covered", GoalHorizon.Yearly, targetPeriod: "2026");
		var uncoveredGoal = new Goal("Uncovered", GoalHorizon.Yearly, targetPeriod: "2026");
		var list = new TaskList("Tasks", contextId: context.Id);
		var notes = new NoteList("Notes", contextId: context.Id);
		db.AddRange(context, coveredGoal, uncoveredGoal, list, notes);
		await db.SaveChangesAsync(ct);

		var completedAtStart = CompletedTask("Completed start", list.Id, PeriodStartUtc, coveredGoal.Id);
		var completedAtEnd = CompletedTask("Completed end", list.Id, AsOfUtc, coveredGoal.Id);
		var reopened = CompletedTask("Reopened", list.Id, PeriodStartUtc.AddHours(1), coveredGoal.Id);
		reopened.Reopen();
		reopened.SetFocusAt(PeriodStartUtc);
		var focusAtEnd = new TaskItem("Focus end", list.Id);
		focusAtEnd.SetFocusAt(AsOfUtc);
		var createdAtStart = new TaskItem("Created start", list.Id);
		var createdAtEnd = new TaskItem("Created end", list.Id);
		var overdue = new TaskItem("Overdue", list.Id, dueAt: PeriodStartUtc.AddDays(-1), goalId: coveredGoal.Id);
		db.AddRange(completedAtStart, completedAtEnd, reopened, focusAtEnd, createdAtStart, createdAtEnd, overdue);
		await db.SaveChangesAsync(ct);
		foreach (var task in new[] { completedAtStart, completedAtEnd, reopened, focusAtEnd, overdue }) SetCreatedAt(db, task, PeriodStartUtc.AddDays(-2));
		SetCreatedAt(db, createdAtStart, PeriodStartUtc);
		SetCreatedAt(db, createdAtEnd, AsOfUtc);

		var recentNote = new NoteItem("Recent", notes.Id);
		var boundaryNote = new NoteItem("Boundary end", notes.Id);
		db.AddRange(recentNote, boundaryNote);
		await db.SaveChangesAsync(ct);
		SetTimestamps(db, recentNote, PeriodStartUtc.AddDays(-1), PeriodStartUtc);
		SetTimestamps(db, boundaryNote, PeriodStartUtc.AddDays(-1), AsOfUtc);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(), ct);

		report.Totals.Should().Be(new ReflectionOverallTotals(1, 1, 1, 1, 1, 0));
		report.Contexts.Should().ContainSingle().Which.Should().Match<ReflectionContextSummary>(summary => summary.CompletedTaskCount == 1 && summary.MissedFocusTaskCount == 1);
		var covered = report.Goals.Single(goal => goal.Id == coveredGoal.Id);
		covered.CompletedTaskCount.Should().Be(1);
		covered.RemainingIncompleteTaskCount.Should().Be(2);
		covered.RemainingOverdueTaskCount.Should().Be(1);
		report.Signals.ActiveGoalsWithoutCompletedWork.Should().ContainSingle(item => item.Id == uncoveredGoal.Id);
		report.Signals.FocusedButIncompleteTasks.Should().ContainSingle(item => item.Id == reopened.Id);
		report.Signals.OverdueCarryoverTasks.Should().ContainSingle(item => item.Id == overdue.Id);
		report.Goals.SelectMany(goal => goal.CompletedTaskSamples).Should().NotContain(item => item.Id == completedAtEnd.Id);
	}

	[Fact]
	public async Task GetAsync_InboxSamplesAreBoundedOrderedAndTruncatedAndArchivedItemsAreExcluded()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var inbox = NoteList.CreateSystem(WellKnownIds.InboxNoteList, "Inbox");
		var archivedNotes = new NoteList("Archive");
		archivedNotes.Archive();
		var archivedTasks = new TaskList("Archive");
		archivedTasks.Archive();
		db.AddRange(inbox, archivedNotes, archivedTasks);
		await db.SaveChangesAsync(ct);
		var pinned = new NoteItem("Pinned", inbox.Id, new string('p', 201));
		pinned.Pin();
		var recent = new NoteItem("Recent", inbox.Id, "short");
		var older = new NoteItem("Older", inbox.Id, "old");
		var hiddenNote = new NoteItem("Hidden note", archivedNotes.Id);
		var hiddenTask = CompletedTask("Hidden task", archivedTasks.Id, PeriodStartUtc);
		db.AddRange(pinned, recent, older, hiddenNote, hiddenTask);
		await db.SaveChangesAsync(ct);
		SetTimestamps(db, pinned, PeriodStartUtc.AddDays(-2), PeriodStartUtc.AddHours(1));
		SetTimestamps(db, recent, PeriodStartUtc.AddDays(-2), PeriodStartUtc.AddHours(3));
		SetTimestamps(db, older, PeriodStartUtc.AddDays(-2), PeriodStartUtc.AddHours(2));
		SetTimestamps(db, hiddenNote, PeriodStartUtc, PeriodStartUtc);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(NoteSampleLimit: 2), ct);

		report.Totals.CurrentInboxNoteCount.Should().Be(3);
		report.Totals.CreatedOrUpdatedNoteCount.Should().Be(3);
		report.Totals.CompletedTaskCount.Should().Be(0);
		report.Inbox.NoteSamples.Select(note => note.Title).Should().Equal("Pinned", "Recent");
		report.Inbox.NoteSamples[0].ContentPreview.Should().HaveLength(200);
		report.Inbox.NoteSamples[0].ContentTruncated.Should().BeTrue();
	}

	[Fact]
	public async Task GetAsync_ExcludesTrashedTasksFromTotalsSignalsAndSamples()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var goal = new Goal("Goal", GoalHorizon.Yearly, targetPeriod: "2026");
		var list = new TaskList("Tasks");
		db.AddRange(goal, list);
		await db.SaveChangesAsync(ct);
		var trashed = CompletedTask("Trashed completion", list.Id, PeriodStartUtc, goal.Id);
		trashed.SetFocusAt(PeriodStartUtc);
		trashed.Trash();
		db.Add(trashed);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(), ct);

		report.Totals.CompletedTaskCount.Should().Be(0);
		report.Contexts.Should().BeEmpty();
		report.Goals.Single(item => item.Id == goal.Id).CompletedTaskSamples.Should().BeEmpty();
		report.Signals.FocusedButIncompleteTaskCount.Should().Be(0);
		report.Signals.OverdueCarryoverTaskCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_ZeroLimitsKeepCountsAndRepresentLegacyContext()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var list = new TaskList("Legacy", contextId: Guid.NewGuid());
		db.Add(list);
		await db.SaveChangesAsync(ct);
		var task = CompletedTask("Done", list.Id, PeriodStartUtc);
		db.Add(task);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(TaskSampleLimit: 0, NoteSampleLimit: 0), ct);

		report.Totals.CompletedTaskCount.Should().Be(1);
		var context = report.Contexts.Should().ContainSingle().Which;
		context.Id.Should().BeNull();
		context.IsMissingOrUnassigned.Should().BeTrue();
		context.CompletedTaskSamples.Should().BeEmpty();
	}

	[Fact]
	public async Task GetAsync_MissedFocusSamplesUseDeterministicOrderingAndBoundedPreviews()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var list = new TaskList("Tasks");
		db.Add(list);
		await db.SaveChangesAsync(ct);
		var ordinary = new TaskItem("Ordinary", list.Id, dueAt: AsOfUtc.AddDays(1));
		ordinary.SetFocusAt(PeriodStartUtc.AddHours(1));
		var important = new TaskItem("Important", list.Id, dueAt: AsOfUtc.AddDays(2), isImportant: true);
		important.SetFocusAt(PeriodStartUtc.AddHours(1));
		var overdue = new TaskItem("Overdue", list.Id, new string('x', 201), dueAt: PeriodStartUtc.AddDays(-1));
		overdue.SetFocusAt(PeriodStartUtc.AddHours(1));
		db.AddRange(ordinary, important, overdue);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new ReflectionReportOptions(TaskSampleLimit: 2), ct);

		report.Signals.FocusedButIncompleteTasks.Select(task => task.Title).Should().Equal("Overdue", "Important");
		report.Signals.FocusedButIncompleteTasks[0].DescriptionPreview.Should().HaveLength(200);
		report.Signals.FocusedButIncompleteTasks[0].DescriptionTruncated.Should().BeTrue();
		report.Signals.FocusedButIncompleteTaskCount.Should().Be(3);
	}

	[Fact]
	public async Task GetAsync_SeparateFactoriesCannotReadAnotherUsersDatabase()
	{
		using var firstDb = CreateDb(out var firstConnection);
		await using var _ = firstConnection;
		using var secondDb = CreateDb(out var secondConnection);
		await using var __ = secondConnection;
		var list = new TaskList("Private");
		firstDb.Add(list);
		await firstDb.SaveChangesAsync(TestContext.Current.CancellationToken);
		firstDb.Add(CompletedTask("First user's completion", list.Id, PeriodStartUtc));
		await firstDb.SaveChangesAsync(TestContext.Current.CancellationToken);

		var first = await CreateReader(firstConnection).GetAsync(new ReflectionReportOptions(), TestContext.Current.CancellationToken);
		var second = await CreateReader(secondConnection).GetAsync(new ReflectionReportOptions(), TestContext.Current.CancellationToken);

		first.Totals.CompletedTaskCount.Should().Be(1);
		second.Totals.CompletedTaskCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_ObservesPreCancelledToken()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		using var source = new CancellationTokenSource();
		source.Cancel();

		var action = () => CreateReader(connection).GetAsync(new ReflectionReportOptions(), source.Token);

		await action.Should().ThrowAsync<OperationCanceledException>();
	}

	private static TaskItem CompletedTask(string title, Guid listId, DateTime completedAt, Guid? goalId = null)
	{
		var task = new TaskItem(title, listId, goalId: goalId);
		task.Complete();
		typeof(TaskItem).GetProperty(nameof(TaskItem.CompletedAt))!.SetValue(task, completedAt);
		return task;
	}

	private static void SetCreatedAt(AppDbContext db, EntityBase entity, DateTime value) => db.Entry(entity).Property(nameof(EntityBase.CreatedAt)).CurrentValue = value;
	private static void SetTimestamps(AppDbContext db, EntityBase entity, DateTime createdAt, DateTime updatedAt)
	{
		db.Entry(entity).Property(nameof(EntityBase.CreatedAt)).CurrentValue = createdAt;
		db.Entry(entity).Property(nameof(EntityBase.UpdatedAt)).CurrentValue = updatedAt;
	}

	private static ReflectionReportReader CreateReader(SqliteConnection connection) => new(new TestPlannerDbContextFactory(connection), new FixedTimeProvider(AsOfUtc));
	private static AppDbContext CreateDb(out SqliteConnection connection)
	{
		connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
		db.Database.EnsureCreated();
		return db;
	}
	private static DateTime Utc(int year, int month, int day, int hour = 0) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
	private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => new(utcNow);
	}

	private sealed class CapturingDbContextFactory(SqliteConnection connection, CommandCaptureInterceptor interceptor) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options));

		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class CommandCaptureInterceptor : DbCommandInterceptor
	{
		public List<string> Commands { get; } = [];

		public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
			DbCommand command,
			CommandEventData eventData,
			InterceptionResult<DbDataReader> result,
			CancellationToken cancellationToken = default)
		{
			Commands.Add(command.CommandText);
			return ValueTask.FromResult(result);
		}
	}
}
