using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Notes;
using OwnPlanner.Infrastructure.Persistence;
using OwnPlanner.Infrastructure.Repositories;

namespace OwnPlanner.Infrastructure.Tests.Notes;

public class NoteListRepositoryTests
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

		var repo = new NoteListRepository(db);
		var list = new NoteList("Personal Notes", "My personal thoughts", "#4CAF50");
		await repo.AddAsync(list);

		var loaded = await repo.GetAsync(list.Id);
		loaded!.Title.Should().Be("Personal Notes");
		loaded.Description.Should().Be("My personal thoughts");
		loaded.Color.Should().Be("#4CAF50");
		loaded.IsArchived.Should().BeFalse();

		loaded.SetTitle("Updated Personal Notes");
		loaded.Archive();
		await repo.UpdateAsync(loaded);
		
		var updated = await repo.GetAsync(list.Id);
		updated!.Title.Should().Be("Updated Personal Notes");
		updated.IsArchived.Should().BeTrue();

		await repo.DeleteAsync(loaded);
		(await repo.GetAsync(list.Id)).Should().BeNull();
	}

	[Fact]
	public async Task List_Filters_Archived_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteListRepository(db);

		var personal = new NoteList("Personal");
		var work = new NoteList("Work");
		var archived = new NoteList("Old Notes");
		archived.Archive();

		await repo.AddAsync(personal);
		await repo.AddAsync(work);
		await repo.AddAsync(archived);

		var all = await repo.ListAsync(true);
		all.Should().HaveCount(3);

		var active = await repo.ListAsync(false);
		active.Should().HaveCount(2);
		active.Should().OnlyContain(x => !x.IsArchived);

		// UpdatedAt ordering desc
		personal.SetDescription("Personal notes");
		await repo.UpdateAsync(personal);
		var ordered = await repo.ListAsync(true);
		ordered.First().Id.Should().Be(personal.Id);
	}

	[Fact]
	public async Task Delete_List_Cascades_To_NoteItems()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;

		var listRepo = new NoteListRepository(db);
		var noteRepo = new NoteItemRepository(db);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list);

		var note = new NoteItem("Note in list", list.Id, "Some content");
		await noteRepo.AddAsync(note);

		var loadedNote = await noteRepo.GetAsync(note.Id);
		loadedNote!.NoteListId.Should().Be(list.Id);

		// Delete the list
		await listRepo.DeleteAsync(list);

		// Note should be deleted due to cascade delete
		var deletedNote = await noteRepo.GetAsync(note.Id);
		deletedNote.Should().BeNull();
	}

	[Fact]
	public async Task ContextId_IsPersistedAndLoaded()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteListRepository(db);

		var contextId = Guid.NewGuid();
		var list = new NoteList("Journal", contextId: contextId);
		await repo.AddAsync(list);

		var loaded = await repo.GetAsync(list.Id);

		loaded!.ContextId.Should().Be(contextId);
	}

	[Fact]
	public async Task List_FiltersByContextId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteListRepository(db);

		var contextA = Guid.NewGuid();
		var contextB = Guid.NewGuid();
		var a1 = new NoteList("A1", contextId: contextA);
		var a2 = new NoteList("A2", contextId: contextA);
		var b1 = new NoteList("B1", contextId: contextB);
		await repo.AddAsync(a1);
		await repo.AddAsync(a2);
		await repo.AddAsync(b1);

		var forA = await repo.ListAsync(false, contextId: contextA);
		forA.Should().HaveCount(2);
		forA.Should().OnlyContain(x => x.ContextId == contextA);

		var forB = await repo.ListAsync(false, contextId: contextB);
		forB.Should().HaveCount(1);
		forB.Single().Id.Should().Be(b1.Id);
	}

	[Fact]
	public async Task List_ExcludesUnassigned_WhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var repo = new NoteListRepository(db);

		var contextId = Guid.NewGuid();
		var assigned1 = new NoteList("Assigned 1", contextId: contextId);
		var assigned2 = new NoteList("Assigned 2", contextId: contextId);
		var legacy = new NoteList("Legacy");
		await repo.AddAsync(assigned1);
		await repo.AddAsync(assigned2);
		await repo.AddAsync(legacy);

		var withoutUnassigned = await repo.ListAsync(false, excludeUnassigned: true);
		withoutUnassigned.Should().HaveCount(2);
		withoutUnassigned.Should().OnlyContain(x => x.ContextId != null);

		var withUnassigned = await repo.ListAsync(false, excludeUnassigned: false);
		withUnassigned.Should().HaveCount(3);
	}
}
