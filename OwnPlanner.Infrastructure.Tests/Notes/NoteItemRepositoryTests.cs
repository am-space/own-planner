using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Notes;

public class NoteItemRepositoryTests
{
	private static AppDbContext CreateDb(out SqliteConnection conn)
	{
		conn = new SqliteConnection("DataSource=:memory:");
		conn.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
		var db = new AppDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	[Fact]
	public async Task Add_Get_Update_Delete_Roundtrip()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);
		
		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);
		
		var item = new NoteItem("Test Note", list.Id, "This is the note content");
		await repo.AddAsync(item, ct);

		var loaded = await repo.GetAsync(item.Id, ct);
		loaded!.Title.Should().Be("Test Note");
		loaded.Content.Should().Be("This is the note content");
		loaded.NoteListId.Should().Be(list.Id);
		loaded.IsPinned.Should().BeFalse();

		loaded.Pin();
		loaded.SetContent("Updated content");
		await repo.UpdateAsync(loaded, ct);
		
		var updated = await repo.GetAsync(item.Id, ct);
		updated!.IsPinned.Should().BeTrue();
		updated.Content.Should().Be("Updated content");

		await repo.DeleteAsync(loaded, ct);
		(await repo.GetAsync(item.Id, ct)).Should().BeNull();
	}

	[Fact]
	public async Task List_Orders_By_Pinned_Then_UpdatedAt()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var noteA = new NoteItem("Note A", list.Id);
		var noteB = new NoteItem("Note B", list.Id);
		var noteC = new NoteItem("Note C", list.Id);
		
		await repo.AddAsync(noteA, ct);
		await repo.AddAsync(noteB, ct);
		await repo.AddAsync(noteC, ct);

		// Pin noteA
		noteA.Pin();
		await repo.UpdateAsync(noteA, ct);

		// Update noteC to make it most recent unpinned
		noteC.SetContent("Updated");
		await repo.UpdateAsync(noteC, ct);

		var all = await repo.ListAsync(ct);
		all.Should().HaveCount(3);

		// Pinned items should come first
		all.First().Id.Should().Be(noteA.Id);
		// Then unpinned items by UpdatedAt desc
		all.Skip(1).First().Id.Should().Be(noteC.Id);
		all.Last().Id.Should().Be(noteB.Id);
	}

	[Fact]
	public async Task ListByNoteList_Filters_By_NoteListId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var noteRepo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list1 = new NoteList("List 1");
		var list2 = new NoteList("List 2");
		await listRepo.AddAsync(list1, ct);
		await listRepo.AddAsync(list2, ct);

		var note1 = new NoteItem("Note in List 1", list1.Id);
		var note2 = new NoteItem("Note in List 2", list2.Id);
		var note3 = new NoteItem("Another note in List 1", list1.Id);
		note3.Pin();

		await noteRepo.AddAsync(note1, ct);
		await noteRepo.AddAsync(note2, ct);
		await noteRepo.AddAsync(note3, ct);

		// Get notes for list1
		var list1Notes = await noteRepo.ListByNoteListAsync(list1.Id, ct);
		list1Notes.Should().HaveCount(2);
		list1Notes.Should().OnlyContain(n => n.NoteListId == list1.Id);

		// Pinned note should come first
		list1Notes.First().Id.Should().Be(note3.Id);

		// Get notes for list2
		var list2Notes = await noteRepo.ListByNoteListAsync(list2.Id, ct);
		list2Notes.Should().HaveCount(1);
		list2Notes.First().Id.Should().Be(note2.Id);
	}

	[Fact]
	public async Task Pin_And_Unpin_Changes_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var noteA = new NoteItem("Note A", list.Id);
		var noteB = new NoteItem("Note B", list.Id);
		
		await repo.AddAsync(noteA, ct);
		await repo.AddAsync(noteB, ct);

		// Initially, noteB should be first (more recent)
		var initial = await repo.ListAsync(ct);
		initial.First().Id.Should().Be(noteB.Id);

		// Pin noteA
		noteA.Pin();
		await repo.UpdateAsync(noteA, ct);

		// Now noteA should be first (pinned)
		var afterPin = await repo.ListAsync(ct);
		afterPin.First().Id.Should().Be(noteA.Id);

		// Unpin noteA
		noteA.Unpin();
		await repo.UpdateAsync(noteA, ct);

		// noteA should still be first (most recently updated)
		var afterUnpin = await repo.ListAsync(ct);
		afterUnpin.First().Id.Should().Be(noteA.Id);
	}

	[Fact]
	public async Task ListByGoalAsync_FiltersBy_GoalId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var otherGoalId = Guid.NewGuid();
		var a = new NoteItem("Note A", list.Id, goalId: goalId);
		var b = new NoteItem("Note B", list.Id, goalId: goalId);
		var c = new NoteItem("Note C", list.Id, goalId: otherGoalId);
		var d = new NoteItem("Note D", list.Id);
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);
		await repo.AddAsync(c, ct);
		await repo.AddAsync(d, ct);

		var result = await repo.ListByGoalAsync(goalId, ct);

		result.Should().HaveCount(2);
		result.Should().OnlyContain(n => n.GoalId == goalId);
	}

	[Fact]
	public async Task ListByGoalAsync_Orders_ByPinnedThenUpdatedAtDesc()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var goalId = Guid.NewGuid();
		var a = new NoteItem("Note A", list.Id, goalId: goalId);
		var b = new NoteItem("Note B", list.Id, goalId: goalId);
		var c = new NoteItem("Note C", list.Id, goalId: goalId);
		await repo.AddAsync(a, ct);
		await repo.AddAsync(b, ct);
		await repo.AddAsync(c, ct);

		a.Pin();
		await repo.UpdateAsync(a, ct);

		c.SetContent("updated");
		await repo.UpdateAsync(c, ct);

		var result = await repo.ListByGoalAsync(goalId, ct);

		result.Should().HaveCount(3);
		result.First().Id.Should().Be(a.Id);
		result.Skip(1).First().Id.Should().Be(c.Id);
		result.Last().Id.Should().Be(b.Id);
	}

	[Fact]
	public async Task ListByGoalAsync_ReturnsEmpty_WhenNoMatchingGoal()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteItemRepository(dbContextFactory);
		var listRepo = new NoteListRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var a = new NoteItem("Note A", list.Id, goalId: Guid.NewGuid());
		await repo.AddAsync(a, ct);

		var result = await repo.ListByGoalAsync(Guid.NewGuid(), ct);

		result.Should().BeEmpty();
	}
}
