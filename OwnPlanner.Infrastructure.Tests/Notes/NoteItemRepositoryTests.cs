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

		var repo = new NoteItemRepository(db);
		var listRepo = new NoteListRepository(db);
		
		var list = new NoteList("Test List");
		await listRepo.AddAsync(list);
		
		var item = new NoteItem("Test Note", list.Id, "This is the note content");
		await repo.AddAsync(item);

		var loaded = await repo.GetAsync(item.Id);
		loaded!.Title.Should().Be("Test Note");
		loaded.Content.Should().Be("This is the note content");
		loaded.NoteListId.Should().Be(list.Id);
		loaded.IsPinned.Should().BeFalse();

		loaded.Pin();
		loaded.SetContent("Updated content");
		await repo.UpdateAsync(loaded);
		
		var updated = await repo.GetAsync(item.Id);
		updated!.IsPinned.Should().BeTrue();
		updated.Content.Should().Be("Updated content");

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(item.Id)).Should().BeNull();
	}

	[Fact]
	public async Task List_Orders_By_Pinned_Then_UpdatedAt()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteItemRepository(db);
		var listRepo = new NoteListRepository(db);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list);

		var noteA = new NoteItem("Note A", list.Id);
		var noteB = new NoteItem("Note B", list.Id);
		var noteC = new NoteItem("Note C", list.Id);
		
		await repo.AddAsync(noteA);
		await repo.AddAsync(noteB);
		await repo.AddAsync(noteC);

		// Pin noteA
		noteA.Pin();
		await repo.UpdateAsync(noteA);

		// Update noteC to make it most recent unpinned
		noteC.SetContent("Updated");
		await repo.UpdateAsync(noteC);

		var all = await repo.ListAsync();
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
		var noteRepo = new NoteItemRepository(db);
		var listRepo = new NoteListRepository(db);

		var list1 = new NoteList("List 1");
		var list2 = new NoteList("List 2");
		await listRepo.AddAsync(list1);
		await listRepo.AddAsync(list2);

		var note1 = new NoteItem("Note in List 1", list1.Id);
		var note2 = new NoteItem("Note in List 2", list2.Id);
		var note3 = new NoteItem("Another note in List 1", list1.Id);
		note3.Pin();

		await noteRepo.AddAsync(note1);
		await noteRepo.AddAsync(note2);
		await noteRepo.AddAsync(note3);

		// Get notes for list1
		var list1Notes = await noteRepo.ListByNoteListAsync(list1.Id);
		list1Notes.Should().HaveCount(2);
		list1Notes.Should().OnlyContain(n => n.NoteListId == list1.Id);

		// Pinned note should come first
		list1Notes.First().Id.Should().Be(note3.Id);

		// Get notes for list2
		var list2Notes = await noteRepo.ListByNoteListAsync(list2.Id);
		list2Notes.Should().HaveCount(1);
		list2Notes.First().Id.Should().Be(note2.Id);
	}

	[Fact]
	public async Task Pin_And_Unpin_Changes_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteItemRepository(db);
		var listRepo = new NoteListRepository(db);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list);

		var noteA = new NoteItem("Note A", list.Id);
		var noteB = new NoteItem("Note B", list.Id);
		
		await repo.AddAsync(noteA);
		await repo.AddAsync(noteB);

		// Initially, noteB should be first (more recent)
		var initial = await repo.ListAsync();
		initial.First().Id.Should().Be(noteB.Id);

		// Pin noteA
		noteA.Pin();
		await repo.UpdateAsync(noteA);

		// Now noteA should be first (pinned)
		var afterPin = await repo.ListAsync();
		afterPin.First().Id.Should().Be(noteA.Id);

		// Unpin noteA
		noteA.Unpin();
		await repo.UpdateAsync(noteA);

		// noteA should still be first (most recently updated)
		var afterUnpin = await repo.ListAsync();
		afterUnpin.First().Id.Should().Be(noteA.Id);
	}
}
