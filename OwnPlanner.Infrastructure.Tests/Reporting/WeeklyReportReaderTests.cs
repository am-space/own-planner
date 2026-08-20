using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public class WeeklyReportReaderTests
{
	private static readonly DateTime AsOfUtc = new(2026, 8, 19, 15, 30, 0, DateTimeKind.Utc);
	private static readonly DateOnly StartDate = new(2026, 8, 17);

	[Fact]
	public async Task GetAsync_EmptyDatabaseUsesCurrentUtcDateAndSevenDayHalfOpenWindow()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;

		var report = await CreateReader(connection).GetAsync(new WeeklyReportOptions(), TestContext.Current.CancellationToken);

		report.AsOfUtc.Should().Be(AsOfUtc);
		report.WindowStartDate.Should().Be(new DateOnly(2026, 8, 19));
		report.WindowEndExclusiveDate.Should().Be(new DateOnly(2026, 8, 26));
		report.TimeZone.Should().Be("UTC");
		report.WindowSemantics.Should().Be("[windowStartDate, windowEndExclusiveDate)");
		report.Days.Should().HaveCount(7);
		report.Totals.Should().Be(new WeeklyOverallTotals(0, 0, 0, 0, 0, 0));
	}

	[Fact]
	public async Task GetAsync_ComputesBoundariesDistinctCountsSummariesAndSignals()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var goal = new Goal("Ship", GoalHorizon.Quarterly, targetPeriod: "2026-Q3");
		var uncoveredGoal = new Goal("Rest", GoalHorizon.Yearly, targetPeriod: "2026");
		var list = new TaskList("Delivery", contextId: context.Id);
		db.AddRange(context, goal, uncoveredGoal, list);
		await db.SaveChangesAsync(ct);

		var overlap = new TaskItem("Focus and due", list.Id, dueAt: Utc(2026, 8, 17, 18), isImportant: true, goalId: goal.Id);
		overlap.SetFocusAt(Utc(2026, 8, 17, 9));
		var endBoundary = new TaskItem("Outside end", list.Id, dueAt: Utc(2026, 8, 24));
		endBoundary.SetFocusAt(Utc(2026, 8, 24));
		var overdue = new TaskItem("Overdue carryover", list.Id, dueAt: Utc(2026, 8, 16), goalId: goal.Id);
		var importantUnscheduled = new TaskItem("Important unscheduled", list.Id, isImportant: true);
		var focusedAtStart = new TaskItem("Start boundary", list.Id, goalId: goal.Id);
		focusedAtStart.SetFocusAt(Utc(2026, 8, 17));
		db.AddRange(overlap, endBoundary, overdue, importantUnscheduled, focusedAtStart);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new WeeklyReportOptions(StartDate, 2, 2), ct);

		report.Totals.Should().Be(new WeeklyOverallTotals(2, 1, 2, 2, 2, 1));
		var firstDay = report.Days[0];
		firstDay.FocusedTaskCount.Should().Be(2);
		firstDay.DueTaskCount.Should().Be(1);
		firstDay.DistinctTaskCount.Should().Be(2);
		firstDay.IsOverloaded.Should().BeTrue();
		firstDay.FocusedTaskSamples.Select(task => task.Id).Should().Contain(overlap.Id);
		report.Contexts.Should().ContainSingle().Which.WindowTaskCount.Should().Be(2);
		report.Goals.Single(item => item.Id == goal.Id).FocusedInsideWindowCount.Should().Be(2);
		report.Signals.ActiveGoalsWithoutFocusedWork.Should().ContainSingle(item => item.Id == uncoveredGoal.Id);
		report.Signals.OverdueTasksNotFocusedInsideWindowCount.Should().Be(1);
		report.Signals.OverdueTasksNotFocusedInsideWindow.Should().ContainSingle(item => item.Id == overdue.Id);
		report.Signals.ImportantTasksWithoutFocusDateCount.Should().Be(1);
		report.Signals.ImportantTasksWithoutFocusDate.Should().ContainSingle(item => item.Id == importantUnscheduled.Id);
	}

	[Fact]
	public async Task GetAsync_ExcludesCompletedAndArchivedListTasksAndRepresentsMissingContext()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var legacyList = new TaskList("Legacy", contextId: Guid.NewGuid());
		var archivedList = new TaskList("Archive");
		archivedList.Archive();
		db.AddRange(legacyList, archivedList);
		await db.SaveChangesAsync(ct);
		var legacy = new TaskItem("Visible legacy", legacyList.Id, dueAt: Utc(2026, 8, 18));
		var hidden = new TaskItem("Hidden archived", archivedList.Id, dueAt: Utc(2026, 8, 18));
		var completed = new TaskItem("Hidden completed", legacyList.Id, dueAt: Utc(2026, 8, 18));
		completed.Complete();
		db.AddRange(legacy, hidden, completed);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new WeeklyReportOptions(StartDate), ct);

		report.Totals.DueInsideWindowCount.Should().Be(1);
		var summary = report.Contexts.Should().ContainSingle().Which;
		summary.Id.Should().BeNull();
		summary.IsMissingOrUnassigned.Should().BeTrue();
		summary.Name.Should().Be("Unassigned or missing context");
		summary.TaskSamples.Should().ContainSingle(item => item.Id == legacy.Id);
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
		var trashed = new TaskItem("Trashed", list.Id, dueAt: Utc(2026, 8, 18), isImportant: true, goalId: goal.Id);
		trashed.SetFocusAt(Utc(2026, 8, 18));
		trashed.Trash();
		db.Add(trashed);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new WeeklyReportOptions(StartDate), ct);

		report.Totals.Should().Be(new WeeklyOverallTotals(0, 0, 0, 0, 0, 0));
		report.Contexts.Should().BeEmpty();
		report.Days.SelectMany(day => day.FocusedTaskSamples).Should().BeEmpty();
		report.Signals.ActiveGoalsWithoutFocusedWork.Should().ContainSingle(item => item.Id == goal.Id);
		report.Signals.ImportantTasksWithoutFocusDateCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_UsesDeterministicOrderingAndBoundsSamples()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var list = new TaskList("Tasks");
		db.Add(list);
		await db.SaveChangesAsync(ct);
		var ordinary = new TaskItem("Ordinary", list.Id, dueAt: Utc(2026, 8, 20, 10));
		ordinary.SetFocusAt(Utc(2026, 8, 18));
		var important = new TaskItem("Important", list.Id, dueAt: Utc(2026, 8, 20, 11), isImportant: true);
		important.SetFocusAt(Utc(2026, 8, 18));
		var overdue = new TaskItem("Overdue", list.Id, new string('x', 201), dueAt: Utc(2026, 8, 16));
		overdue.SetFocusAt(Utc(2026, 8, 18));
		db.AddRange(ordinary, important, overdue);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new WeeklyReportOptions(StartDate, 2), ct);
		var samples = report.Days[1].FocusedTaskSamples;

		samples.Select(item => item.Title).Should().Equal("Overdue", "Important");
		samples[0].DescriptionPreview.Should().HaveLength(200);
		samples[0].DescriptionTruncated.Should().BeTrue();
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
		firstDb.Add(new TaskItem("First user's task", list.Id, dueAt: Utc(2026, 8, 18)));
		await firstDb.SaveChangesAsync(TestContext.Current.CancellationToken);

		var first = await CreateReader(firstConnection).GetAsync(new WeeklyReportOptions(StartDate), TestContext.Current.CancellationToken);
		var second = await CreateReader(secondConnection).GetAsync(new WeeklyReportOptions(StartDate), TestContext.Current.CancellationToken);

		first.Totals.DueInsideWindowCount.Should().Be(1);
		second.Totals.DueInsideWindowCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_ObservesPreCancelledToken()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		using var source = new CancellationTokenSource();
		source.Cancel();

		var action = () => CreateReader(connection).GetAsync(new WeeklyReportOptions(StartDate), source.Token);

		await action.Should().ThrowAsync<OperationCanceledException>();
	}

	private static WeeklyReportReader CreateReader(SqliteConnection connection) =>
		new(new TestPlannerDbContextFactory(connection), new FixedTimeProvider(AsOfUtc));

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
}
