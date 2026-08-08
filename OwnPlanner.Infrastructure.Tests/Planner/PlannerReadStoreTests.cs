using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Application.Planner;
using OwnPlanner.Domain.Contexts;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Planner;

namespace OwnPlanner.Infrastructure.Tests.Planner;

public class PlannerReadStoreTests
{
	[Fact]
	public async Task QueryTasksAsync_AppliesFiltersPagingAndBoundedProjection()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var planningContext = new PlanningContext("Work", ContextType.Area);
		var taskList = new TaskList("Launch list", contextId: planningContext.Id);
		var otherList = new TaskList("Other list");
		var goal = new Goal("Release", GoalHorizon.Quarterly, targetPeriod: "2026-Q3");
		db.AddRange(planningContext, taskList, otherList, goal);
		await db.SaveChangesAsync(ct);

		var longDescription = $"Launch {new string('x', PlannerReadDefaults.PreviewLength + 20)}";
		var first = new TaskItem("Launch production", taskList.Id, longDescription, isImportant: true, goalId: goal.Id);
		first.SetFocusAt(new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc));
		var second = new TaskItem("Launch follow-up", taskList.Id, "Launch review", isImportant: true, goalId: goal.Id);
		second.SetFocusAt(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));
		var completed = new TaskItem("Launch completed", taskList.Id, isImportant: true, goalId: goal.Id);
		completed.Complete();
		var unrelated = new TaskItem("Private errand", otherList.Id);
		db.AddRange(first, second, completed, unrelated);
		await db.SaveChangesAsync(ct);

		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection));
		var result = await store.QueryTasksAsync(
			new PlannerTaskQuery(
				Search: "LAUNCH",
				Status: PlannerTaskStatus.Open,
				ImportantOnly: true,
				TaskListId: taskList.Id,
				ContextId: planningContext.Id,
				GoalId: goal.Id,
				Limit: 1),
			ct);

		result.TotalCount.Should().Be(2);
		result.Items.Should().ContainSingle();
		result.HasMore.Should().BeTrue();
		var item = result.Items[0];
		item.Id.Should().Be(first.Id);
		item.DescriptionPreview.Should().HaveLength(PlannerReadDefaults.PreviewLength);
		item.TaskListName.Should().Be("Launch list");
		item.ContextName.Should().Be("Work");
		item.GoalName.Should().Be("Release");

		var detail = await store.GetTaskAsync(first.Id, ct);
		detail.Should().NotBeNull();
		detail!.Description.Should().Be(longDescription);
		detail.ContextName.Should().Be("Work");
	}

	[Fact]
	public async Task QueryGoalsAsync_AppliesStatusHorizonSearchAndPaging()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var quarterly = new Goal(
			"Launch product",
			GoalHorizon.Quarterly,
			$"Roadmap {new string('x', PlannerReadDefaults.PreviewLength + 20)}",
			"2026-Q3");
		var achieved = new Goal("Launch archive", GoalHorizon.Monthly, targetPeriod: "2026-07");
		achieved.SetStatus(GoalStatus.Achieved);
		db.AddRange(quarterly, achieved);
		await db.SaveChangesAsync(ct);

		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection));
		var result = await store.QueryGoalsAsync(
			new PlannerGoalQuery(
				Search: "launch",
				Status: PlannerGoalStatus.All,
				Horizon: GoalHorizon.Quarterly,
				Limit: 25),
			ct);

		result.TotalCount.Should().Be(1);
		result.Items.Should().ContainSingle(item => item.Id == quarterly.Id);
		result.Items[0].DescriptionPreview.Should().HaveLength(PlannerReadDefaults.PreviewLength);
		(await store.GetGoalAsync(quarterly.Id, ct))!.Description.Should().StartWith("Roadmap");
	}

	[Fact]
	public async Task QueryNotesAsync_AppliesFiltersAndLoadsFullDetailSeparately()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var planningContext = new PlanningContext("Research", ContextType.Project);
		var noteList = new NoteList("Reading", contextId: planningContext.Id);
		var goal = new Goal("Learn", GoalHorizon.Yearly, targetPeriod: "2026");
		db.AddRange(planningContext, noteList, goal);
		await db.SaveChangesAsync(ct);

		var content = $"Architecture {new string('x', PlannerReadDefaults.PreviewLength + 20)}";
		var pinned = new NoteItem("System design", noteList.Id, content, goal.Id);
		pinned.Pin();
		var unpinned = new NoteItem("Architecture draft", noteList.Id, "Architecture", goal.Id);
		db.AddRange(pinned, unpinned);
		await db.SaveChangesAsync(ct);

		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection));
		var result = await store.QueryNotesAsync(
			new PlannerNoteQuery(
				Search: "ARCHITECTURE",
				PinnedOnly: true,
				NoteListId: noteList.Id,
				ContextId: planningContext.Id,
				GoalId: goal.Id),
			ct);

		result.Items.Should().ContainSingle(item => item.Id == pinned.Id);
		result.Items[0].ContentPreview.Should().HaveLength(PlannerReadDefaults.PreviewLength);
		result.Items[0].ContextName.Should().Be("Research");
		result.Items[0].GoalName.Should().Be("Learn");
		(await store.GetNoteAsync(pinned.Id, ct))!.Content.Should().Be(content);
	}

	[Fact]
	public async Task GetDetailsAsync_UnknownIdentifiersReturnNull()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection));

		(await store.GetTaskAsync(Guid.NewGuid(), ct)).Should().BeNull();
		(await store.GetGoalAsync(Guid.NewGuid(), ct)).Should().BeNull();
		(await store.GetNoteAsync(Guid.NewGuid(), ct)).Should().BeNull();
	}

	[Fact]
	public async Task GetFilterOptionsAsync_IncludesArchivedAndInactiveRelationshipMetadata()
	{
		using var db = CreateDb(out var connection);
		await using var _ = connection;
		var ct = TestContext.Current.CancellationToken;
		var taskList = new TaskList("Archived tasks");
		taskList.Archive();
		var noteList = new NoteList("Archived notes");
		noteList.Archive();
		var planningContext = new PlanningContext("Past project", ContextType.Project);
		planningContext.SetStatus(ContextStatus.Archived);
		var goal = new Goal("Shipped", GoalHorizon.Yearly, targetPeriod: "2025");
		goal.SetStatus(GoalStatus.Achieved);
		db.AddRange(taskList, noteList, planningContext, goal);
		await db.SaveChangesAsync(ct);

		var store = new PlannerReadStore(new TestPlannerDbContextFactory(connection));
		var result = await store.GetFilterOptionsAsync(ct);

		result.TaskLists.Should().ContainSingle(option => option.Id == taskList.Id && option.IsArchived);
		result.NoteLists.Should().ContainSingle(option => option.Id == noteList.Id && option.IsArchived);
		result.Contexts.Should().ContainSingle(option => option.Id == planningContext.Id && option.Status == ContextStatus.Archived);
		result.Goals.Should().ContainSingle(option => option.Id == goal.Id && option.Status == GoalStatus.Achieved);
	}

	private static AppDbContext CreateDb(out SqliteConnection connection)
	{
		connection = new SqliteConnection("DataSource=:memory:");
		connection.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
		var db = new AppDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}
}
