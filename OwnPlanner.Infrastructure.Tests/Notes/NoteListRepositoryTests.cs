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
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var repo = new NoteListRepository(dbContextFactory);
		var list = new NoteList("Personal Notes", "My personal thoughts", "#4CAF50");
		await repo.AddAsync(list, ct);

		var loaded = await repo.GetAsync(list.Id, ct);
		loaded!.Title.Should().Be("Personal Notes");
		loaded.Description.Should().Be("My personal thoughts");
		loaded.Color.Should().Be("#4CAF50");
		loaded.IsArchived.Should().BeFalse();

		loaded.SetTitle("Updated Personal Notes");
		loaded.Archive();
		await repo.UpdateAsync(loaded, ct);
		
		var updated = await repo.GetAsync(list.Id, ct);
		updated!.Title.Should().Be("Updated Personal Notes");
		updated.IsArchived.Should().BeTrue();

		await repo.DeleteAsync(loaded, ct);
		(await repo.GetAsync(list.Id, ct)).Should().BeNull();
	}

	[Fact]
	public async Task List_Filters_Archived_And_Ordering()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteListRepository(dbContextFactory);

		var personal = new NoteList("Personal");
		var work = new NoteList("Work");
		var archived = new NoteList("Old Notes");
		archived.Archive();

		await repo.AddAsync(personal, ct);
		await repo.AddAsync(work, ct);
		await repo.AddAsync(archived, ct);

		var all = await repo.ListAsync(true, ct: ct);
		all.Should().HaveCount(3);

		var active = await repo.ListAsync(false, ct: ct);
		active.Should().HaveCount(2);
		active.Should().OnlyContain(x => !x.IsArchived);

		// UpdatedAt ordering desc
		personal.SetDescription("Personal notes");
		await repo.UpdateAsync(personal, ct);
		var ordered = await repo.ListAsync(true, ct: ct);
		ordered.First().Id.Should().Be(personal.Id);
	}

	[Fact]
	public async Task Delete_List_Cascades_To_NoteItems()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);

		var listRepo = new NoteListRepository(dbContextFactory);
		var noteRepo = new NoteItemRepository(dbContextFactory);

		var list = new NoteList("Test List");
		await listRepo.AddAsync(list, ct);

		var note = new NoteItem("Note in list", list.Id, "Some content");
		await noteRepo.AddAsync(note, ct);

		var loadedNote = await noteRepo.GetAsync(note.Id, ct);
		loadedNote!.NoteListId.Should().Be(list.Id);

		// Delete the list
		await listRepo.DeleteAsync(list, ct);

		// Note should be deleted due to cascade delete
		var deletedNote = await noteRepo.GetAsync(note.Id, ct);
		deletedNote.Should().BeNull();
	}

	[Fact]
	public async Task ContextId_IsPersistedAndLoaded()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteListRepository(dbContextFactory);

		var contextId = Guid.NewGuid();
		var list = new NoteList("Journal", contextId: contextId);
		await repo.AddAsync(list, ct);

		var loaded = await repo.GetAsync(list.Id, ct);

		loaded!.ContextId.Should().Be(contextId);
	}

	[Fact]
	public async Task List_FiltersByContextId()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteListRepository(dbContextFactory);

		var contextA = Guid.NewGuid();
		var contextB = Guid.NewGuid();
		var a1 = new NoteList("A1", contextId: contextA);
		var a2 = new NoteList("A2", contextId: contextA);
		var b1 = new NoteList("B1", contextId: contextB);
		await repo.AddAsync(a1, ct);
		await repo.AddAsync(a2, ct);
		await repo.AddAsync(b1, ct);

		var forA = await repo.ListAsync(false, contextId: contextA, ct: ct);
		forA.Should().HaveCount(2);
		forA.Should().OnlyContain(x => x.ContextId == contextA);

		var forB = await repo.ListAsync(false, contextId: contextB, ct: ct);
		forB.Should().HaveCount(1);
		forB.Single().Id.Should().Be(b1.Id);
	}

	[Fact]
	public async Task List_ExcludesUnassigned_WhenRequested()
	{
		using var db = CreateDb(out var conn);
		await using var _ = conn;
		var ct = TestContext.Current.CancellationToken;
		var dbContextFactory = new TestPlannerDbContextFactory(conn);
		var repo = new NoteListRepository(dbContextFactory);

		var contextId = Guid.NewGuid();
		var assigned1 = new NoteList("Assigned 1", contextId: contextId);
		var assigned2 = new NoteList("Assigned 2", contextId: contextId);
		var legacy = new NoteList("Legacy");
		await repo.AddAsync(assigned1, ct);
		await repo.AddAsync(assigned2, ct);
		await repo.AddAsync(legacy, ct);

		var withoutUnassigned = await repo.ListAsync(false, excludeUnassigned: true, ct: ct);
		withoutUnassigned.Should().HaveCount(2);
		withoutUnassigned.Should().OnlyContain(x => x.ContextId != null);

		var withUnassigned = await repo.ListAsync(false, excludeUnassigned: false, ct: ct);
		withUnassigned.Should().HaveCount(3);
	}
}
