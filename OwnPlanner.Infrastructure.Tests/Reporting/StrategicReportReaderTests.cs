using FluentAssertions;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OwnPlanner.Application.Reporting;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public class StrategicReportReaderTests
{
	private static readonly DateTime AsOfUtc = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

	[Fact]
	public async Task GetAsync_EmptyDatabaseReturnsEmptyReport()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;

		var report = await CreateReader(connection).GetAsync(
			new StrategicReportOptions(),
			TestContext.Current.CancellationToken);

		report.Totals.Should().Be(new StrategicOverallTotals(0, 0, 0, 0, 0, 0, 0, 0));
		report.Contexts.Should().BeEmpty();
		report.Goals.Should().BeEmpty();
	}

	[Fact]
	public async Task GetAsync_ComputesTotalsContextGoalAndSignals()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var work = new PlanningContext("Work", ContextType.Area);
		var empty = new PlanningContext("Empty", ContextType.Project);
		var activeGoal = new Goal("Ship", GoalHorizon.Quarterly, targetPeriod: "2026-Q3", metric: "Release");
		activeGoal.SetMetricCurrent("Beta");
		var orphanGoal = new Goal("Orphan", GoalHorizon.Yearly, targetPeriod: "2026");
		var inactiveGoal = new Goal("Achieved", GoalHorizon.Yearly, targetPeriod: "2026");
		inactiveGoal.SetStatus(GoalStatus.Achieved);
		var taskList = new TaskList("Delivery", contextId: work.Id);
		var noteList = new NoteList("Briefs", contextId: work.Id);
		db.AddRange(work, empty, activeGoal, orphanGoal, inactiveGoal, taskList, noteList);
		await db.SaveChangesAsync(ct);

		var overdue = new TaskItem("Overdue", taskList.Id, dueAt: AsOfUtc.AddMinutes(-1), isImportant: true, goalId: activeGoal.Id);
		var boundary = new TaskItem("Boundary", taskList.Id, dueAt: AsOfUtc);
		var unassigned = new TaskItem("No goal", taskList.Id);
		var staleGoal = new TaskItem("Deleted goal", taskList.Id, goalId: Guid.NewGuid());
		var inactiveGoalTask = new TaskItem("Achieved goal", taskList.Id, goalId: inactiveGoal.Id);
		var completed = new TaskItem("Done", taskList.Id, isImportant: true, goalId: activeGoal.Id);
		completed.Complete();
		var note = new NoteItem("Decision", noteList.Id, "Ship it", activeGoal.Id);
		db.AddRange(overdue, boundary, unassigned, staleGoal, inactiveGoalTask, completed, note);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(), ct);

		report.AsOfUtc.Should().Be(AsOfUtc);
		report.Totals.Should().Be(new StrategicOverallTotals(2, 2, 1, 1, 5, 1, 1, 1));
		var context = report.Contexts.Single(item => item.Id == work.Id);
		context.Should().Match<StrategicContextSummary>(item =>
			item.TaskListCount == 1 && item.NoteListCount == 1 && item.IncompleteTaskCount == 5 &&
			item.ImportantIncompleteTaskCount == 1 && item.OverdueIncompleteTaskCount == 1 &&
			item.GoalLinkedTaskCount == 2 && item.NoteCount == 1);
		var goal = report.Goals.Single(item => item.Id == activeGoal.Id);
		goal.IncompleteTaskCount.Should().Be(1);
		goal.LinkedNoteCount.Should().Be(1);
		goal.DistinctContextCount.Should().Be(1);
		goal.DistinctTaskListCount.Should().Be(1);
		report.Signals.ActiveGoalsWithoutActiveTasks.Should().ContainSingle(item => item.Id == orphanGoal.Id);
		report.Signals.ContextsWithoutActiveTasks.Should().ContainSingle(item => item.Id == empty.Id);
		report.Signals.ContextsWithoutTaskOrNoteLists.Should().ContainSingle(item => item.Id == empty.Id);
		report.Signals.TasksWithoutGoalCount.Should().Be(3);
		report.Signals.TasksWithoutGoal.Should().Contain(task => task.Id == staleGoal.Id);
	}

	[Fact]
	public async Task GetAsync_StructuralSignalsHaveDeterministicNameOrdering()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		db.AddRange(
			new PlanningContext("Zulu", ContextType.Area),
			new PlanningContext("alpha", ContextType.Area),
			new Goal("Zulu", GoalHorizon.Yearly, targetPeriod: "2026"),
			new Goal("alpha", GoalHorizon.Yearly, targetPeriod: "2026"));
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(), ct);

		report.Signals.ActiveGoalsWithoutActiveTasks.Select(item => item.Name).Should().Equal("alpha", "Zulu");
		report.Signals.ContextsWithoutActiveTasks.Select(item => item.Name).Should().Equal("alpha", "Zulu");
		report.Signals.ContextsWithoutTaskOrNoteLists.Select(item => item.Name).Should().Equal("alpha", "Zulu");
	}

	[Fact]
	public async Task GetAsync_ExcludesArchivedListsAndArchivedContexts()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var archivedContext = new PlanningContext("Archive", ContextType.Area);
		archivedContext.SetStatus(ContextStatus.Archived);
		var archivedTasks = new TaskList("Old tasks", contextId: archivedContext.Id);
		archivedTasks.Archive();
		var archivedNotes = new NoteList("Old notes", contextId: archivedContext.Id);
		archivedNotes.Archive();
		db.AddRange(archivedContext, archivedTasks, archivedNotes);
		await db.SaveChangesAsync(ct);
		db.AddRange(new TaskItem("Hidden", archivedTasks.Id), new NoteItem("Hidden", archivedNotes.Id));
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(), ct);

		report.Totals.Should().Be(new StrategicOverallTotals(0, 0, 0, 0, 0, 0, 0, 0));
		report.Contexts.Should().BeEmpty();
	}

	[Fact]
	public async Task GetAsync_ExcludesTrashedTasksFromTotalsSignalsAndSamples()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var goal = new Goal("Goal", GoalHorizon.Yearly, targetPeriod: "2026");
		var list = new TaskList("Tasks", contextId: context.Id);
		db.AddRange(context, goal, list);
		await db.SaveChangesAsync(ct);
		var trashed = new TaskItem("Trashed", list.Id, dueAt: AsOfUtc.AddDays(-1), isImportant: true, goalId: goal.Id);
		trashed.Trash();
		db.Add(trashed);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(), ct);

		report.Totals.IncompleteTaskCount.Should().Be(0);
		report.Contexts.Single().IncompleteTaskCount.Should().Be(0);
		report.Contexts.Single().TaskSamples.Should().BeEmpty();
		report.Signals.ActiveGoalsWithoutActiveTasks.Should().ContainSingle(item => item.Id == goal.Id);
		report.Signals.TasksWithoutGoalCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_BoundsOrdersAndTruncatesSamples()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var taskList = new TaskList("Tasks", contextId: context.Id);
		var noteList = new NoteList("Notes", contextId: context.Id);
		db.AddRange(context, taskList, noteList);
		await db.SaveChangesAsync(ct);
		var ordinary = new TaskItem("Ordinary", taskList.Id, new string('x', 201), dueAt: AsOfUtc.AddDays(1));
		var important = new TaskItem("Important", taskList.Id, dueAt: AsOfUtc.AddDays(2), isImportant: true);
		var overdue = new TaskItem("Overdue", taskList.Id, new string('t', 201), dueAt: AsOfUtc.AddDays(-1));
		var pinned = new NoteItem("Pinned", noteList.Id, new string('n', 201));
		pinned.Pin();
		var recent = new NoteItem("Recent", noteList.Id, "short");
		var bulkTasks = Enumerable.Range(1, 25).Select(index => new TaskItem($"Bulk {index:D2}", taskList.Id)).ToArray();
		db.AddRange(ordinary, important, overdue, pinned, recent);
		db.AddRange(bulkTasks);
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(2, 1), ct);
		var summary = report.Contexts.Single();

		summary.TaskSamples.Select(task => task.Title).Should().Equal("Overdue", "Important");
		summary.IncompleteTaskCount.Should().Be(28);
		summary.TaskSamples[0].DescriptionPreview.Should().HaveLength(200);
		summary.TaskSamples[0].DescriptionTruncated.Should().BeTrue();
		summary.NoteSamples.Should().ContainSingle().Which.Title.Should().Be("Pinned");
		summary.NoteSamples[0].ContentPreview.Should().HaveLength(200);
		summary.NoteSamples[0].ContentTruncated.Should().BeTrue();
	}

	[Fact]
	public async Task GetAsync_ZeroLimitsReturnCountsWithoutSamplesAndHandleMissingContext()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var list = new TaskList("Legacy", contextId: Guid.NewGuid());
		db.Add(list);
		await db.SaveChangesAsync(ct);
		db.Add(new TaskItem("Legacy task", list.Id));
		await db.SaveChangesAsync(ct);

		var report = await CreateReader(connection).GetAsync(new StrategicReportOptions(0, 0), ct);

		report.Totals.IncompleteTaskCount.Should().Be(1);
		report.Signals.TasksWithoutGoalCount.Should().Be(1);
		report.Signals.TasksWithoutGoal.Should().BeEmpty();
	}

	[Fact]
	public async Task GetAsync_ZeroLimitsNeverSelectPersonalContentColumns()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var taskList = new TaskList("Tasks", contextId: context.Id);
		var noteList = new NoteList("Notes", contextId: context.Id);
		db.AddRange(context, taskList, noteList);
		await db.SaveChangesAsync(ct);
		db.AddRange(
			new TaskItem("Large task", taskList.Id, new string('t', 100_000)),
			new NoteItem("Large note", noteList.Id, new string('n', 100_000)));
		await db.SaveChangesAsync(ct);
		var interceptor = new CommandCaptureInterceptor();
		var reader = new StrategicReportReader(new CapturingDbContextFactory(connection, interceptor), new FixedTimeProvider(AsOfUtc));

		var report = await reader.GetAsync(new StrategicReportOptions(0, 0), ct);

		report.Totals.IncompleteTaskCount.Should().Be(1);
		report.Totals.NoteCount.Should().Be(1);
		interceptor.Commands.Should().NotContain(command => command.Contains("Description", StringComparison.Ordinal));
		interceptor.Commands.Should().NotContain(command => command.Contains("Content", StringComparison.Ordinal));
	}

	[Fact]
	public async Task GetAsync_SampleQueriesTruncatePersonalContentInSql()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var context = new PlanningContext("Work", ContextType.Area);
		var taskList = new TaskList("Tasks", contextId: context.Id);
		var noteList = new NoteList("Notes", contextId: context.Id);
		db.AddRange(context, taskList, noteList);
		await db.SaveChangesAsync(ct);
		db.AddRange(
			new TaskItem("Large task", taskList.Id, new string('t', 100_000)),
			new NoteItem("Large note", noteList.Id, new string('n', 100_000)));
		await db.SaveChangesAsync(ct);
		var interceptor = new CommandCaptureInterceptor();
		var reader = new StrategicReportReader(new CapturingDbContextFactory(connection, interceptor), new FixedTimeProvider(AsOfUtc));

		var report = await reader.GetAsync(new StrategicReportOptions(1, 1), ct);

		report.Signals.TasksWithoutGoal.Should().ContainSingle().Which.DescriptionPreview.Should().HaveLength(200);
		var contentQueries = interceptor.Commands.Where(command =>
			command.Contains("Description", StringComparison.Ordinal) || command.Contains("Content", StringComparison.Ordinal));
		contentQueries.Should().HaveCount(2).And.OnlyContain(command =>
			command.Contains("substr(", StringComparison.OrdinalIgnoreCase) &&
			command.Contains("length(", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task GetAsync_ObservesPreCancelledToken()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		using var source = new CancellationTokenSource();
		source.Cancel();

		var action = () => CreateReader(connection).GetAsync(new StrategicReportOptions(), source.Token);

		await action.Should().ThrowAsync<OperationCanceledException>();
	}

	private static StrategicReportReader CreateReader(SqliteConnection connection) =>
		new(new TestPlannerDbContextFactory(connection), new FixedTimeProvider(AsOfUtc));

	private static AppDbContext CreateDb(out SqliteConnection connection)
	{
		connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
		db.Database.EnsureCreated();
		return db;
	}

	private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => new(utcNow);
	}

	private sealed class CapturingDbContextFactory(
		SqliteConnection connection,
		CommandCaptureInterceptor interceptor) : IPlannerDbContextFactory
	{
		public ValueTask<AppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new AppDbContext(
				new DbContextOptionsBuilder<AppDbContext>()
					.UseSqlite(connection)
					.AddInterceptors(interceptor)
					.Options));

		public Task DeleteUserDatabaseAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class CommandCaptureInterceptor : DbCommandInterceptor
	{
		public List<string> Commands { get; } = [];

		public override InterceptionResult<DbDataReader> ReaderExecuting(
			DbCommand command,
			CommandEventData eventData,
			InterceptionResult<DbDataReader> result)
		{
			Commands.Add(command.CommandText);
			return result;
		}

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
