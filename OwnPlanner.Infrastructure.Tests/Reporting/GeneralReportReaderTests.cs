using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain;
using OwnPlanner.Domain.Goals;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Domain.Tasks;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Reporting;

namespace OwnPlanner.Infrastructure.Tests.Reporting;

public sealed class GeneralReportReaderTests
{
	[Fact]
	public async Task GetAsync_ExcludesArchivedAndTrashedData_UsesInboxMembershipAndCurrentCompletion()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var connection = new SqliteConnection("DataSource=:memory:");
		await connection.OpenAsync(ct);
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
		await db.Database.EnsureCreatedAsync(ct);
		var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
		var inbox = TaskList.CreateSystem(WellKnownIds.InboxTaskList, "Inbox");
		var notes = NoteList.CreateSystem(WellKnownIds.InboxNoteList, "Inbox");
		var archive = new TaskList("Archive"); archive.Archive();
		var otherNotes = new NoteList("Filed");
		var activeGoal = new Goal("Active", GoalHorizon.Yearly, targetPeriod: "2026");
		var archivedTask = new TaskItem("Excluded archive", archive.Id, dueAt: now);
		var trashed = new TaskItem("Excluded trash", inbox.Id, dueAt: now); trashed.Trash();
		var completed = new TaskItem("Completed focus", inbox.Id, dueAt: now, goalId: activeGoal.Id);
		completed.SetFocusAt(now); completed.Complete();
		var visible = new TaskItem("Visible", inbox.Id, "PRIVATE DESCRIPTION", now); visible.SetFocusAt(now);
		db.AddRange(inbox, notes, archive, otherNotes, activeGoal, archivedTask, trashed, completed, visible,
			new TaskItem("Unscheduled", inbox.Id), new NoteItem("Capture", notes.Id, "PRIVATE BODY"), new NoteItem("Filed", otherNotes.Id));
		await db.SaveChangesAsync(ct);
		var reader = new GeneralReportReader(new TestPlannerDbContextFactory(connection), new FixedTimeProvider(now));

		var report = await reader.GetAsync(ct);

		report.Today.FocusTaskCount.Should().Be(2);
		report.Today.CompletedCount.Should().Be(1);
		report.Commitments.DueTodayCount.Should().Be(1);
		report.Inbox.UnscheduledTaskCount.Should().Be(1);
		report.Inbox.UnreviewedCaptureCount.Should().Be(1);
		report.Direction.Goals.Should().ContainSingle(g => g.Id == activeGoal.Id);
		JsonSerializer.Serialize(report).Should().NotContain("PRIVATE").And.NotContain("Excluded");
		db.ChangeTracker.HasChanges().Should().BeFalse();
		(await db.TaskItems.CountAsync(ct)).Should().Be(5);
		notes.Archive(); await db.SaveChangesAsync(ct);
		(await reader.GetAsync(ct)).Inbox.UnreviewedCaptureCount.Should().Be(0);
	}

	[Fact]
	public async Task GetAsync_EmptyDatabaseAndCancellation()
	{
		var ct = TestContext.Current.CancellationToken;
		await using var connection = new SqliteConnection("DataSource=:memory:");
		await connection.OpenAsync(ct);
		await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
		await db.Database.EnsureCreatedAsync(ct);
		var reader = new GeneralReportReader(new TestPlannerDbContextFactory(connection), TimeProvider.System);
		var report = await reader.GetAsync(ct);
		report.Tasks.Should().BeEmpty();
		report.Today.FocusTaskCount.Should().Be(0);
		using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
		var action = () => reader.GetAsync(cancellation.Token);
		await action.Should().ThrowAsync<OperationCanceledException>();
	}

	private sealed class FixedTimeProvider(DateTime now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => new(now);
	}
}
